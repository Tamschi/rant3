﻿using Rant.Stringes;

namespace Rant.Engine.Syntax.Expressions.Operators
{
    internal class REAModuloOperator : REAInfixOperator
    {
        public REAModuloOperator(Stringe _origin)
			: base(_origin)
		{
            Operation = (x, y) => x % y;
            Precedence = 5;
        }
    }
}
