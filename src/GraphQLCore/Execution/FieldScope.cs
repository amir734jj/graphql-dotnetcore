﻿namespace GraphQLCore.Execution
{
    using GraphQLCore.Type.Directives;
    using Language.AST;
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Dynamic;
    using System.Linq;
    using System.Linq.Expressions;
    using System.Reflection;
    using Type;
    using Type.Complex;
    using Type.Translation;
    using Utils;

    public class FieldScope
    {
        private List<GraphQLArgument> arguments;
        private IFieldCollector fieldCollector;
        private object parent;
        private GraphQLObjectType type;
        private ISchemaRepository schemaRepository;
        private IVariableResolver variableResolver;

        public FieldScope(
            ISchemaRepository schemaRepository,
            IVariableResolver variableResolver,
            IFieldCollector fieldCollector,
            GraphQLObjectType type,
            object parent)
        {
            this.type = type;
            this.parent = parent;
            this.arguments = new List<GraphQLArgument>();

            this.fieldCollector = fieldCollector;
            this.schemaRepository = schemaRepository;
            this.variableResolver = variableResolver;
        }

        public object CompleteValue(
            object input,
            Type inputType,
            GraphQLFieldSelection selection,
            IList<GraphQLArgument> arguments)
        {
            if (input == null || inputType == null)
                return null;

            if (ReflectionUtilities.IsDescendant(inputType, typeof(GraphQLUnionType)))
            {
                var unionSchemaType = this.schemaRepository.GetSchemaTypeFor(inputType) as GraphQLUnionType;
                return this.CompleteValue(input, unionSchemaType.ResolveType(input), selection, arguments);
            }

            if (ReflectionUtilities.IsDescendant(inputType, typeof(GraphQLObjectType)))
                return this.CompleteObjectType((GraphQLObjectType)input, selection, arguments, this.parent);

            if (ReflectionUtilities.IsCollection(inputType))
                return this.CompleteCollectionType((IEnumerable)input, selection, arguments);

            var schemaValue = this.schemaRepository.GetSchemaTypeFor(inputType);
            if (schemaValue is GraphQLObjectType)
            {
                return this.CompleteObjectType((GraphQLObjectType)schemaValue, selection, arguments, input);
            }

            if (ReflectionUtilities.IsEnum(inputType))
                return input.ToString();

            return input;
        }

        public object[] FetchArgumentValues(LambdaExpression expression, IList<GraphQLArgument> arguments)
        {
            return ReflectionUtilities.GetParameters(expression)
                .Select(e => this.GetValueForArgument(arguments, e))
                .ToArray();
        }

        public object GetArgumentValue(IEnumerable<GraphQLArgument> arguments, string argumentName, GraphQLInputType type)
        {
            var argument = arguments.SingleOrDefault(e => e.Name.Value == argumentName);

            if (argument == null)
                return null;

            return type.GetFromAst(argument.Value, this.schemaRepository);
        }

        public dynamic GetObject(Dictionary<string, IList<GraphQLFieldSelection>> fields)
        {
            var result = new ExpandoObject();
            var dictionary = (IDictionary<string, object>)result;

            foreach (var field in fields)
                this.AddFieldsFromSelectionToResultDictionary(dictionary, field.Key, field.Value);

            return result;
        }

        public object InvokeWithArguments(IList<GraphQLArgument> arguments, LambdaExpression expression)
        {
            var argumentValues = this.FetchArgumentValues(expression, arguments);

            return expression.Compile().DynamicInvoke(argumentValues);
        }

        private void AddFieldsFromSelectionToResultDictionary(
            IDictionary<string, object> dictionary, string fieldName, IList<GraphQLFieldSelection> fieldSelections)
        {
            foreach (var selection in fieldSelections)
                this.AddToResultDictionaryIfNotAlreadyPresent(dictionary, fieldName, selection);

            foreach (var selection in fieldSelections)
                this.ApplyDirectives(dictionary, fieldName, selection);
        }

        private object GetValueForArgument(IList<GraphQLArgument> arguments, ParameterExpression e)
        {
            if (this.IsContextType(e))
                return this.CreateContextObject(e.Type);

            return ReflectionUtilities.ChangeValueType(
                this.GetArgumentValue(
                    arguments,
                    e.Name,
                    this.schemaRepository.GetSchemaInputTypeFor(e.Type)),
                    e.Type);
        }

        private bool IsContextType(ParameterExpression e)
        {
            var contextType = typeof(IContext<>);

            return e.Type.GetTypeInfo().IsGenericType && e.Type.GetGenericTypeDefinition() == contextType;
        }

        private object CreateContextObject(Type type)
        {
            var genericArgument = type.GetTypeInfo()
                .GetGenericArguments()
                .Single();

            var fieldContextType = typeof(FieldContext<>)
                .MakeGenericType(genericArgument);

            return Activator.CreateInstance(fieldContextType, this.parent);
        }

        private void ApplyDirectives(
            IDictionary<string, object> dictionary,
            string fieldName,
            GraphQLFieldSelection selection)
        {
            if (dictionary.ContainsKey(fieldName))
            {
                foreach (var directive in selection.Directives)
                {
                    var directiveType = this.schemaRepository.GetDirective(directive.Name.Value);

                    if (!directiveType.PostExecutionIncludeFieldIntoResult(
                            directive, this.schemaRepository, dictionary[fieldName], (ExpandoObject)dictionary))
                            dictionary.Remove(fieldName);
                }
            }
        }

        private void AddToResultDictionaryIfNotAlreadyPresent(
            IDictionary<string, object> dictionary,
            string fieldName,
            GraphQLFieldSelection selection)
        {
            if (!dictionary.ContainsKey(fieldName))
            {
                 dictionary.Add(
                     fieldName,
                    this.GetDefinitionAndExecuteField(this.type, selection, dictionary));
            }
        }

        private object CompleteCollectionType(IEnumerable input, GraphQLFieldSelection selection, IList<GraphQLArgument> arguments)
        {
            var result = new List<object>();
            foreach (var element in input)
                result.Add(this.CompleteValue(element, element?.GetType(), selection, arguments));

            return result;
        }

        private object CompleteObjectType(
            GraphQLObjectType input,
            GraphQLFieldSelection selection,
            IList<GraphQLArgument> arguments,
            object parentObject)
        {
            var scope = new FieldScope(
                this.schemaRepository,
                this.variableResolver,
                this.fieldCollector,
                input,
                parentObject);

            scope.arguments = arguments.ToList();

            return scope.GetObject(this.fieldCollector.CollectFields(input, selection.SelectionSet));
        }

        private List<GraphQLArgument> GetArgumentsFromSelection(GraphQLFieldSelection selection)
        {
            var arguments = this.arguments.ToList();

            arguments.RemoveAll(e => selection.Arguments.Any(arg => arg.Name.Value.Equals(e.Name.Value)));
            arguments.AddRange(selection.Arguments);

            return arguments;
        }

        private object GetDefinitionAndExecuteField(
            GraphQLObjectType type,
            GraphQLFieldSelection selection,
            IDictionary<string, object> dictionary)
        {
            var arguments = this.GetArgumentsFromSelection(selection);
            var fieldInfo = this.GetFieldInfo(type, selection);
            var directivesToUse = selection.Directives;

            var result = this.ResolveField(fieldInfo, arguments, this.parent);
            foreach (var directive in selection.Directives)
            {
                var directiveType = this.schemaRepository.GetDirective(directive.Name.Value);

                if (directiveType != null && directiveType.Locations.Any(l => l == DirectiveLocation.FIELD))
                {
                    result = this.InvokeWithArguments(
                        directive.Arguments.ToList(),
                        directiveType.GetResolver(result, (ExpandoObject)dictionary));
                }
            }

            var resultType = fieldInfo?.SystemType ?? result?.GetType();
            return this.CompleteValue(result, resultType, selection, arguments);
        }

        private GraphQLObjectTypeFieldInfo GetFieldInfo(GraphQLObjectType type, GraphQLFieldSelection selection)
        {
            var name = this.GetFieldName(selection);
            return type.GetFieldInfo(name);
        }

        private string GetFieldName(GraphQLFieldSelection selection)
        {
            return selection.Name?.Value ?? selection.Alias?.Value;
        }

        private object ProcessField(object input)
        {
            if (input == null)
                return null;

            if (ReflectionUtilities.IsEnum(input.GetType()))
                return input.ToString();

            return input;
        }

        private object ResolveField(
            GraphQLObjectTypeFieldInfo fieldInfo, IList<GraphQLArgument> arguments, object parent)
        {
            if (fieldInfo == null)
                return null;

            if (fieldInfo.IsResolver)
                return this.ProcessField(this.InvokeWithArguments(arguments, fieldInfo.Lambda));

            return this.ProcessField(fieldInfo.Lambda.Compile().DynamicInvoke(new object[] { parent }));
        }
    }
}