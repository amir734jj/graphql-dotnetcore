﻿using System.Collections.Generic;

namespace GraphQLCore.Language.AST
{
    public class GraphQLFragmentSpread : ASTNode, IWithDirectives
    {
        public IEnumerable<GraphQLDirective> Directives { get; set; }

        public override ASTNodeKind Kind
        {
            get
            {
                return ASTNodeKind.FragmentSpread;
            }
        }

        public GraphQLName Name { get; set; }
    }
}