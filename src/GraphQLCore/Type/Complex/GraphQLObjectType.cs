﻿namespace GraphQLCore.Type
{
    using Complex;
    using Exceptions;
    using Execution;
    using System.Collections.Generic;
    using System.Linq;
    using System.Linq.Expressions;
    using System.Reflection;

    public abstract class GraphQLObjectType : GraphQLComplexType
    {
        public override System.Type SystemType { get; protected set; }

        public GraphQLObjectType(string name, string description) : base(name, description)
        {
            this.Fields = new Dictionary<string, GraphQLObjectTypeFieldInfo>();
            this.SystemType = this.GetType();
        }

        public FieldDefinitionBuilder Field(string fieldName, LambdaExpression fieldLambda)
        {
            return this.AddField(fieldName, fieldLambda);
        }

        protected virtual FieldDefinitionBuilder AddField(string fieldName, LambdaExpression resolver)
        {
            if (this.ContainsField(fieldName))
                throw new GraphQLException("Can't insert two fields with the same name.");

            this.ValidateResolver(resolver);

            var fieldInfo = this.CreateResolverFieldInfo(fieldName, resolver);

            this.Fields.Add(fieldName, fieldInfo);

            return new FieldDefinitionBuilder(fieldInfo);
        }

        private void ValidateResolver(LambdaExpression resolver)
        {
            var contextType = typeof(IContext<>);

            var contextParameters = resolver.Parameters
                .Where(e => e.Type.GetTypeInfo().IsGenericType && e.Type.GetGenericTypeDefinition() == contextType);

            foreach (var context in contextParameters)
            {
                var argumentType = context.Type.GetTypeInfo().GetGenericArguments().Single();

                if (argumentType != this.SystemType)
                {
                    throw new GraphQLException(
                        $"Can't specify IContext of type \"{argumentType.Name}\" in GraphQLObjectType with type \"{this.SystemType}\"");
                }
            }
        }

        private GraphQLObjectTypeFieldInfo CreateResolverFieldInfo(string fieldName, LambdaExpression resolver)
        {
            return GraphQLObjectTypeFieldInfo.CreateResolverFieldInfo(fieldName, resolver);
        }

        private GraphQLObjectTypeFieldInfo CreateUnionResolverFieldInfo(string fieldName, System.Type unionType, LambdaExpression resolver)
        {
            return GraphQLObjectTypeFieldInfo.CreateUnionResolverFieldInfo(fieldName, unionType, resolver);
        }
    }
}