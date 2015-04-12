﻿using Rant.Engine.Syntax;
using Rant.Stringes;
using System.Collections.Generic;

namespace Rant.Engine.Compiler
{
	internal class RantCompiler
	{
		private readonly string _source;
		private readonly string _sourceName;
		private readonly IEnumerable<Token<R>> _tokens;
		private readonly TokenReader _reader;
		private readonly Stack<RantFunctionInfo> _funcCalls = new Stack<RantFunctionInfo>();
		private readonly Stack<RantQueryInfo> _queries = new Stack<RantQueryInfo>();

		private RantCompiler(string sourceName, string source)
		{
			_sourceName = sourceName;
			_source = source;
			_tokens = RantLexer.GenerateTokens(source.ToStringe());
			_reader = new TokenReader(sourceName, _tokens);
		}

		private enum ReadType
		{
			/// <summary>
			/// Reads a list of items and returns an RASequence.
			/// </summary>
			Sequence,
			/// <summary>
			/// Reads a list of items and returns an RABlock.
			/// </summary>
			Block,
			/// <summary>
			/// Reads a list of arguments and returns an RAFunction.
			/// </summary>
			FuncCall,
			/// <summary>
			/// Reads a query and returns a RAQuery
			/// </summary>
			Query
		}

		public static RantAction Compile(string sourceName, string source)
		{
			return new RantCompiler(sourceName, source).Read(ReadType.Sequence);
		}

		private RantAction Read(ReadType type, Token<R> fromToken = null)
		{
			// Stores actions for a single block item, argument, etc.
			var actions = new List<RantAction>();
			// Stores sequences of actions for blocks, function calls, etc.
			var sequences = new List<RantAction>();

			Token<R> token = null;

			// ok let's do this
			while (!_reader.End)
			{
				token = _reader.ReadToken();

				switch (token.ID)
				{
					case R.EOF:
						goto done;

					// Escape sequence
					case R.EscapeSequence:
						actions.Add(new RAEscape(token));
						break;

					case R.LeftSquare:
						{
							var nameToken = _reader.ReadLoose(R.Text, "function name");
							string name = nameToken.Value;
							var func = RantFunctions.GetFunction(name);
							if (func == null)
								throw new RantCompilerException(_sourceName, nameToken, $"Unknown function: '{name}'");
							var argList = new List<RantAction>();
							if (_reader.TakeLoose(R.Colon))
							{
								_funcCalls.Push(func);
								actions.Add(Read(ReadType.FuncCall, token));
							}
							else
							{
								var end = _reader.Read(R.RightSquare);
								VerifyArgCount(func, 0, token, end);
								actions.Add(new RAFunction(Stringe.Range(token, end), func, argList));
							}
							break;
						}

					case R.RightSquare:
						{
							if (type != ReadType.FuncCall)
								Unexpected(token);
							// Add item to args
							sequences.Add(actions.Count == 1 ? actions[0] : new RASequence(actions));
							var func = _funcCalls.Pop();
							VerifyArgCount(func, sequences.Count, fromToken, token);
							// TODO: Add support for function overloads
							return new RAFunction(Stringe.Range(fromToken, token), func, sequences);
						}

					// Argument separator
					case R.Semicolon:
						if (type != ReadType.FuncCall) goto default;
						// Add item to args
						sequences.Add(actions.Count == 1 ? actions[0] : new RASequence(actions));
						actions.Clear();
						break;

					// Block
					case R.LeftCurly:
						actions.Add(Read(ReadType.Block, token));
						break;

					// Block item boundary
					case R.Pipe:
					case R.RightCurly:
						// Wrong mode?
						if (type != ReadType.Block)
						{
							// Throw an error if it's '}'
							if (token.ID == R.RightCurly)
								Unexpected(token);
							// If it's a slash, just print it.
							goto default;
						}

						// Add item to block
						sequences.Add(actions.Count == 1 ? actions[0] : new RASequence(actions));
						if (token.ID == R.RightCurly) return new RABlock(sequences);
						actions.Clear();
						break;

					// Constant literals
					case R.ConstantLiteral:
						actions.Add(new RAText(token, Util.UnescapeConstantLiteral(token.Value)));
						break;

					case R.LeftAngle:
						{
							var name = _reader.ReadLoose(R.Text);
							_queries.Push(new RantQueryInfo(name));
							actions.Add(Read(ReadType.Query, token));
						}
						break;
					case R.Subtype:
						{
							if (type != ReadType.Query) goto default;
							var subtype = _reader.ReadLoose(R.Text);
							_queries.Peek().Subtype = subtype;
						}
						break;
					case R.Dollar:
						{
							if (type != ReadType.Query) goto default;
							var nextToken = _reader.PeekToken();
							if (nextToken.ID == R.Subtype)
								throw new RantCompilerException(_sourceName, token, "The query exclusivity operator can only be placed after the name and subtype of a query.");
							_queries.Peek().IsExclusive = true;
						}
						break;
					case R.Hyphen:
						{
							if (type != ReadType.Query) goto default;
							var nextToken = _reader.PeekToken();
							Stringe className;
							bool negative = false;
							if (nextToken.ID == R.Exclamation)
							{
								negative = true;
								_reader.ReadToken();
								if (_queries.Peek().IsExclusive)
									throw new RantCompilerException(_sourceName, token, "You can't define a negative class filter in an exclusive query.");
							}
							className = _reader.ReadLoose(R.Text);
							_queries.Peek().ClassFilters.Add(new _<bool, string>[] { new _<bool, string>(!negative, className.Value) });
						}
						break;
					case R.RightAngle:
						return new RAQuery(_queries.Pop());

					// Plain text
					default:
						actions.Add(new RAText(token));
						break;
				}
			}

			done:

			switch (type)
			{
				case ReadType.Sequence:
					return new RASequence(actions);
				case ReadType.Block:
					throw new RantCompilerException(_sourceName, fromToken, "Unterminated block found.");
				default:
					throw new RantCompilerException(_sourceName, token, "Unexpected end of file.");
			}
		}

		private void VerifyArgCount(RantFunctionInfo func, int argc, Stringe from, Stringe to)
		{
			if (argc != func.Parameters.Length)
				throw new RantCompilerException(_sourceName, Stringe.Range(from, to),
					$"The function '{func.Name}' requires '{func.Parameters.Length}' argument(s).");
		}

		private void Unexpected(Stringe token)
		{
			throw new RantCompilerException(_sourceName, token, $"Unexpected token: '{token.Value}'");
		}
	}
}