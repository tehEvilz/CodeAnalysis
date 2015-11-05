﻿using System;
using System.Collections.Generic;
using System.Linq;
using CodeAnalysis.DataClasses;
using CodeAnalysis.Enums;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeAnalysis.Domain
{
    internal class LawOfDemeterValidator : ICodeAnalyzer
    {
        private readonly DocumentWalker _documentWalker = new DocumentWalker();

        public IEnumerable<OptimizationRecomendation> Analyze(Solution solution)
        {
            var documents = _documentWalker.GetAllDocumentsFromSolution(solution);
            return documents.Select(GetLODViolations);
        }

        // TODO SymbolInfo von IEnumerable Typen werden nicht gefunden. Wieso? -> Eventuell Token des GenericNames
        // TODO Erweiterungsmethoden werden nicht erkannt -> Erster Parameter in Liste ist this
        // TODO Was ist mit List[0].Foo() -> theoretisch Verstoß, aber wohl nicht sinnvoll
        // TODO Was ist mit ...ToString().Split('').... -> Grunddatentypen; Listen, LINQ OK
        private OptimizationRecomendation GetLODViolations(Document document)
        {
            var semanticModel = document.GetSemanticModelAsync().Result;
            var methodInvocations = _documentWalker.GetNodesFromDocument<InvocationExpressionSyntax>(document);
            var lodViolations =
                (from methodInvocation in methodInvocations
                    where !IsInvocationOfContainingType(methodInvocation, semanticModel)
                    where !IsInvocationOfContainingMethodsParameters(methodInvocation, semanticModel)
                    where !IsInvocationOfContainingTypesMembers(methodInvocation, semanticModel)
                    where !IsInvocationOfInMethodCreatedObject(methodInvocation, semanticModel)
                    where !IsStaticInvocation(methodInvocation, semanticModel)
                 select methodInvocation).ToList();
            return _documentWalker.CreateRecommendations(document, lodViolations, RecommendationType.LODViolation);
        }

        private bool IsInvocationOfContainingType(InvocationExpressionSyntax invocation, SemanticModel model)
        {
            var invocationSymbol = model.GetSymbolInfo(invocation).Symbol;
            var containingType = _documentWalker.GetContainingNodeOfType<TypeDeclarationSyntax>(invocation);
            var methodDeclarations = containingType.DescendantNodes().OfType<MethodDeclarationSyntax>();
            return methodDeclarations
                .Select(declaration => model.GetDeclaredSymbol(declaration))
                .Any(declarationSymbol => invocationSymbol.Equals(declarationSymbol));
        }

        private bool IsInvocationOfContainingMethodsParameters(InvocationExpressionSyntax invocation,
            SemanticModel model)
        {
            var invocationSymbol = model.GetSymbolInfo(invocation).Symbol;
            var containingMethod = _documentWalker.GetContainingNodeOfType<MethodDeclarationSyntax>(invocation);
            var parameters = containingMethod.ParameterList.Parameters;
            return IsSymbolInvocationOfNodes(parameters, invocationSymbol, model);
        }

        private bool IsInvocationOfContainingTypesMembers(InvocationExpressionSyntax invocation, SemanticModel model)
        {
            var invocationSymbol = model.GetSymbolInfo(invocation).Symbol;
            var containingType = _documentWalker.GetContainingNodeOfType<TypeDeclarationSyntax>(invocation);

            var members = containingType.Members; 
            //members.AddRange(containingType.Members.OfType<BaseFieldDeclarationSyntax>());
            //members.AddRange(containingType.Members.OfType<BasePropertyDeclarationSyntax>());

            var br = false;
            if (containingType.ToString().Contains("NameInspector") &&
                !containingType.ToString().Contains("LawOfDemeterValidator")
                && invocation.ToString().Contains("_documentWalker.GetNodesFromDocument"))
            {
                br = true;
            }

            //            var fields = containingType.Members.OfType<FieldDeclarationSyntax>();
            //            var properties = containingType.Members.OfType<PropertyDeclarationSyntax>();
            return IsSymbolInvocationOfNodes(members, invocationSymbol, model);
        }

        private bool IsInvocationOfInMethodCreatedObject(InvocationExpressionSyntax invocation, SemanticModel model)
        {
            var invocationSymbol = model.GetSymbolInfo(invocation).Symbol;
            var containingMethod = _documentWalker.GetContainingNodeOfType<MethodDeclarationSyntax>(invocation);
            var objectCreations = containingMethod.DescendantNodes().OfType<LocalDeclarationStatementSyntax>();
            return IsSymbolInvocationOfNodes(objectCreations, invocationSymbol, model);
        }

        private bool IsStaticInvocation(InvocationExpressionSyntax invocation, SemanticModel model)
        {
            var invocationSymbol = model.GetSymbolInfo(invocation).Symbol;
            return invocationSymbol.IsStatic;
        }


        private bool IsSymbolInvocationOfNodes(IEnumerable<SyntaxNode> nodes, ISymbol invocationSymbol, SemanticModel model )
        {
            return (from objectTypes in nodes.Select(x => x.DescendantNodes().Where
                (t => t is IdentifierNameSyntax || t is PredefinedTypeSyntax || t is GenericNameSyntax || t is ArrayTypeSyntax))
                from type in objectTypes
                select FindSymbolInfo(model, type)
                into symbolInfo
                where symbolInfo != null
                select CollectAllMembers(symbolInfo)).Any(member => member.Contains(invocationSymbol));
        }

        private ITypeSymbol FindSymbolInfo(SemanticModel model, SyntaxNode parameter)
        {
            //var symbol = model.GetSymbolInfo(parameter).Symbol;
            //if(symbol is INamedTypeSymbol|| symbol is IArrayTypeSymbol) return (ITypeSymbol)symbol;

            return model.GetSymbolInfo(parameter).Symbol as ITypeSymbol;
        }

        private List<ISymbol> CollectAllMembers(ITypeSymbol symbolInfo)
        {
            var members = symbolInfo.GetMembers().ToList();
            var parent = symbolInfo.BaseType;
            while (parent != null)
            {
                members.AddRange(parent.GetMembers());
                parent = parent.BaseType;
            }
            return members;
        }
    }
}
 