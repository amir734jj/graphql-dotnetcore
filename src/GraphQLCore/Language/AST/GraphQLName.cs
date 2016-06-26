﻿namespace GraphQLCore.Language.AST
{
    public class GraphQLName : ASTNode
    {
        public override ASTNodeKind Kind
        {
            get
            {
                return ASTNodeKind.Name;
            }
        }

        public string Value { get; internal set; }
    }
}