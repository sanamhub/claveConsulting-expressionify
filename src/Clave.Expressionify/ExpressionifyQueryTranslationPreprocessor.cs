﻿using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.Internal;

namespace Clave.Expressionify {
    public class ExpressionifyQueryTranslationPreprocessor : QueryTranslationPreprocessor {
        private readonly QueryTranslationPreprocessor _innerPreprocessor;

        public ExpressionifyQueryTranslationPreprocessor(
            QueryTranslationPreprocessor innerPreprocessor,
            QueryTranslationPreprocessorDependencies dependencies,
            QueryCompilationContext compilationContext)
            : base(dependencies, compilationContext) {
            _innerPreprocessor = innerPreprocessor;
        }

        public override Expression Process(Expression query) {
            var visitor = new ExpressionifyVisitor();
            query = visitor.Visit(query);

            if (visitor.HasReplacedCalls)
                query = EvaluateExpression(query);

            return _innerPreprocessor.Process(query);
        }

        private Expression EvaluateExpression(Expression query) {
            // 1) Ensure that no new parameters are introduced when creating the query
            // 2) This expression visitor also makes slight optimzations, like replacing evaluatable expressions.
            var visitor = new ParameterExtractingExpressionVisitor(
                Dependencies.EvaluatableExpressionFilter,
                new ThrowOnParameterAccess(),
                QueryCompilationContext.ContextType,
                QueryCompilationContext.Model,
                QueryCompilationContext.Logger,
                parameterize: true,
                generateContextAccessors: false);

            return visitor.ExtractParameters(query);
        }

        private class ThrowOnParameterAccess : IParameterValues {
            public void AddParameter(string name, object? value)
                => throw new InvalidOperationException(
                    "Adding parameters in a cached query context is not allowed. " +
                    $"Explicitly call .{nameof(ExpressionifyExtension.Expressionify)}() on the query or use {nameof(ExpressionEvaluationMode)}.{nameof(ExpressionEvaluationMode.FullCompatibilityButSlow)}.");

            public IReadOnlyDictionary<string, object?> ParameterValues
                => throw new InvalidOperationException(
                    "Accessing parameters in a cached query context is not allowed. " +
                    $"Explicitly call .{nameof(ExpressionifyExtension.Expressionify)}() on the query or use {nameof(ExpressionEvaluationMode)}.{nameof(ExpressionEvaluationMode.FullCompatibilityButSlow)}.");
        }
    }
}
