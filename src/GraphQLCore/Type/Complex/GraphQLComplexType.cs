﻿namespace GraphQLCore.Type
{
    using Complex;
    using Execution;
    using GraphQLCore.Exceptions;
    using Introspection;
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Linq.Expressions;
    using System.Reflection;
    using Translation;

    public abstract class GraphQLComplexType : GraphQLBaseType, ISystemTypeBound
    {
        public override bool IsLeafType
        {
            get
            {
                return false;
            }
        }

        public abstract Type SystemType { get; protected set; }

        public GraphQLComplexType(string name, string description) : base(name, description)
        {
            this.Fields = new Dictionary<string, GraphQLObjectTypeFieldInfo>();
        }

        protected Dictionary<string, GraphQLObjectTypeFieldInfo> Fields { get; set; }

        public bool ContainsField(string fieldName)
        {
            return this.Fields.ContainsKey(fieldName);
        }

        public GraphQLObjectTypeFieldInfo GetFieldInfo(string fieldName)
        {
            if (fieldName == "__typename")
            {
                Expression<Func<string>> lambda = () => this.Name;
                return GraphQLObjectTypeFieldInfo.CreateResolverFieldInfo("__typename", lambda);
            }

            if (!this.ContainsField(fieldName))
                return null;

            return this.Fields[fieldName];
        }

        public GraphQLObjectTypeFieldInfo[] GetFieldsInfo()
        {
            return this.Fields
                .Select(e => e.Value)
                .ToArray();
        }

        protected void ValidateResolver(LambdaExpression resolver)
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

        public override IntrospectedType Introspect(ISchemaRepository schemaRepository)
        {
            var introspectedType = new ComplexIntrospectedType(schemaRepository, this);
            introspectedType.Name = this.Name;
            introspectedType.Description = this.Description;
            introspectedType.Kind = TypeKind.OBJECT;

            return introspectedType;
        }

        protected GraphQLObjectTypeFieldInfo CreateFieldInfo<T, TProperty>(string fieldName, Expression<Func<T, TProperty>> accessor)
        {
            return GraphQLObjectTypeFieldInfo.CreateAccessorFieldInfo(fieldName, accessor);
        }
    }
}