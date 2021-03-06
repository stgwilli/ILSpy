// Copyright (c) AlphaSierraPapa for the SharpDevelop Team (for details please see \doc\copyright.txt)
// This code is distributed under MIT X11 license (for details please see \doc\license.txt)

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

using ICSharpCode.NRefactory.CSharp.PatternMatching;
using ICSharpCode.NRefactory.TypeSystem;

namespace ICSharpCode.NRefactory.CSharp
{
	/// <summary>
	/// Outputs the AST.
	/// </summary>
	public class OutputVisitor : IPatternAstVisitor<object, object>
	{
		readonly IOutputFormatter formatter;
		readonly CSharpFormattingPolicy policy;
		
		readonly Stack<AstNode> containerStack = new Stack<AstNode>();
		readonly Stack<AstNode> positionStack = new Stack<AstNode>();
		
		/// <summary>
		/// Used to insert the minimal amount of spaces so that the lexer recognizes the tokens that were written.
		/// </summary>
		LastWritten lastWritten;
		
		enum LastWritten
		{
			Whitespace,
			Other,
			KeywordOrIdentifier,
			Plus,
			Minus,
			Ampersand,
			QuestionMark,
			Division
		}
		
		public OutputVisitor(TextWriter textWriter, CSharpFormattingPolicy formattingPolicy)
		{
			if (textWriter == null)
				throw new ArgumentNullException("textWriter");
			if (formattingPolicy == null)
				throw new ArgumentNullException("formattingPolicy");
			this.formatter = new TextWriterOutputFormatter(textWriter);
			this.policy = formattingPolicy;
		}
		
		public OutputVisitor(IOutputFormatter formatter, CSharpFormattingPolicy formattingPolicy)
		{
			if (formatter == null)
				throw new ArgumentNullException("formatter");
			if (formattingPolicy == null)
				throw new ArgumentNullException("formattingPolicy");
			this.formatter = formatter;
			this.policy = formattingPolicy;
		}
		
		#region StartNode/EndNode
		void StartNode(AstNode node)
		{
			// Ensure that nodes are visited in the proper nested order.
			// Jumps to different subtrees are allowed only for the child of a placeholder node.
			Debug.Assert(containerStack.Count == 0 || node.Parent == containerStack.Peek() || containerStack.Peek().NodeType == NodeType.Placeholder);
			if (positionStack.Count > 0)
				WriteSpecialsUpToNode(node);
			containerStack.Push(node);
			positionStack.Push(node.FirstChild);
			formatter.StartNode(node);
		}
		
		object EndNode(AstNode node)
		{
			Debug.Assert(node == containerStack.Peek());
			AstNode pos = positionStack.Pop();
			Debug.Assert(pos == null || pos.Parent == node);
			WriteSpecials(pos, null);
			containerStack.Pop();
			formatter.EndNode(node);
			return null;
		}
		#endregion
		
		#region WriteSpecials
		/// <summary>
		/// Writes all specials from start to end (exclusive). Does not touch the positionStack.
		/// </summary>
		void WriteSpecials(AstNode start, AstNode end)
		{
			for (AstNode pos = start; pos != end; pos = pos.NextSibling) {
				if (pos.Role == AstNode.Roles.Comment) {
					pos.AcceptVisitor(this, null);
				}
			}
		}
		
		/// <summary>
		/// Writes all specials between the current position (in the positionStack) and the next
		/// node with the specified role. Advances the current position.
		/// </summary>
		void WriteSpecialsUpToRole(Role role)
		{
			for (AstNode pos = positionStack.Peek(); pos != null; pos = pos.NextSibling) {
				if (pos.Role == role) {
					WriteSpecials(positionStack.Pop(), pos);
					positionStack.Push(pos);
					break;
				}
			}
		}
		
		/// <summary>
		/// Writes all specials between the current position (in the positionStack) and the specified node.
		/// Advances the current position.
		/// </summary>
		void WriteSpecialsUpToNode(AstNode node)
		{
			for (AstNode pos = positionStack.Peek(); pos != null; pos = pos.NextSibling) {
				if (pos == node) {
					WriteSpecials(positionStack.Pop(), pos);
					positionStack.Push(pos);
					break;
				}
			}
		}
		
		void WriteSpecialsUpToRole(Role role, AstNode nextNode)
		{
			// Look for the role between the current position and the nextNode.
			for (AstNode pos = positionStack.Peek(); pos != null && pos != nextNode; pos = pos.NextSibling) {
				if (pos.Role == AstNode.Roles.Comma) {
					WriteSpecials(positionStack.Pop(), pos);
					positionStack.Push(pos);
					break;
				}
			}
		}
		#endregion
		
		#region Comma
		/// <summary>
		/// Writes a comma.
		/// </summary>
		/// <param name="nextNode">The next node after the comma.</param>
		/// <param name="noSpacesAfterComma">When set prevents printing a space after comma.</param>
		void Comma(AstNode nextNode, bool noSpaceAfterComma = false)
		{
			WriteSpecialsUpToRole(AstNode.Roles.Comma, nextNode);
			Space(policy.SpacesBeforeComma);
			formatter.WriteToken(",");
			lastWritten = LastWritten.Other;
			Space(!noSpaceAfterComma && policy.SpacesAfterComma);
		}
		
		void WriteCommaSeparatedList(IEnumerable<AstNode> list)
		{
			bool isFirst = true;
			foreach (AstNode node in list) {
				if (isFirst) {
					isFirst = false;
				} else {
					Comma(node);
				}
				node.AcceptVisitor(this, null);
			}
		}
		
		void WriteCommaSeparatedListInParenthesis(IEnumerable<AstNode> list, bool spaceWithin)
		{
			LPar();
			if (list.Any()) {
				Space(spaceWithin);
				WriteCommaSeparatedList(list);
				Space(spaceWithin);
			}
			RPar();
		}
		
		#if DOTNET35
		void WriteCommaSeparatedList(IEnumerable<VariableInitializer> list)
		{
			WriteCommaSeparatedList(list.SafeCast<VariableInitializer, AstNode>());
		}
		
		void WriteCommaSeparatedList(IEnumerable<AstType> list)
		{
			WriteCommaSeparatedList(list.SafeCast<AstType, AstNode>());
		}
		
		void WriteCommaSeparatedListInParenthesis(IEnumerable<Expression> list, bool spaceWithin)
		{
			WriteCommaSeparatedListInParenthesis(list.SafeCast<Expression, AstNode>(), spaceWithin);
		}
		
		void WriteCommaSeparatedListInParenthesis(IEnumerable<ParameterDeclaration> list, bool spaceWithin)
		{
			WriteCommaSeparatedListInParenthesis(list.SafeCast<ParameterDeclaration, AstNode>(), spaceWithin);
		}
		#endif
		
		void WriteCommaSeparatedListInBrackets(IEnumerable<Expression> list)
		{
			WriteToken("[", AstNode.Roles.LBracket);
			if (list.Any()) {
				Space(policy.SpacesWithinBrackets);
				WriteCommaSeparatedList(list.SafeCast<Expression, AstNode>());
				Space(policy.SpacesWithinBrackets);
			}
			WriteToken("]", AstNode.Roles.RBracket);
		}
		#endregion
		
		#region Write tokens
		/// <summary>
		/// Writes a keyword, and all specials up to
		/// </summary>
		void WriteKeyword(string keyword, Role<CSharpTokenNode> tokenRole = null)
		{
			WriteSpecialsUpToRole(tokenRole ?? AstNode.Roles.Keyword);
			if (lastWritten == LastWritten.KeywordOrIdentifier)
				formatter.Space();
			formatter.WriteKeyword(keyword);
			lastWritten = LastWritten.KeywordOrIdentifier;
		}
		
		void WriteIdentifier(string identifier, Role<Identifier> identifierRole = null)
		{
			WriteSpecialsUpToRole(identifierRole ?? AstNode.Roles.Identifier);
			if (IsKeyword(identifier, containerStack.Peek())) {
				if (lastWritten == LastWritten.KeywordOrIdentifier)
					Space(); // this space is not strictly required, so we call Space()
				formatter.WriteToken("@");
			} else if (lastWritten == LastWritten.KeywordOrIdentifier) {
				formatter.Space(); // this space is strictly required, so we directly call the formatter
			}
			formatter.WriteIdentifier(identifier);
			lastWritten = LastWritten.KeywordOrIdentifier;
		}
		
		void WriteToken(string token, Role<CSharpTokenNode> tokenRole)
		{
			WriteSpecialsUpToRole(tokenRole);
			// Avoid that two +, - or ? tokens are combined into a ++, -- or ?? token.
			// Note that we don't need to handle tokens like = because there's no valid
			// C# program that contains the single token twice in a row.
			// (for +, - and &, this can happen with unary operators;
			// for ?, this can happen in "a is int? ? b : c" or "a as int? ?? 0";
			// and for /, this can happen with "1/ *ptr" or "1/ //comment".)
			if (lastWritten == LastWritten.Plus && token[0] == '+'
			    || lastWritten == LastWritten.Minus && token[0] == '-'
			    || lastWritten == LastWritten.Ampersand && token[0] == '&'
			    || lastWritten == LastWritten.QuestionMark && token[0] == '?'
			    || lastWritten == LastWritten.Division && token[0] == '*')
			{
				formatter.Space();
			}
			formatter.WriteToken(token);
			if (token == "+")
				lastWritten = LastWritten.Plus;
			else if (token == "-")
				lastWritten = LastWritten.Minus;
			else if (token == "&")
				lastWritten = LastWritten.Ampersand;
			else if (token == "?")
				lastWritten = LastWritten.QuestionMark;
			else if (token == "/")
				lastWritten = LastWritten.Division;
			else
				lastWritten = LastWritten.Other;
		}
		
		void LPar()
		{
			WriteToken("(", AstNode.Roles.LPar);
		}
		
		void RPar()
		{
			WriteToken(")", AstNode.Roles.LPar);
		}
		
		/// <summary>
		/// Marks the end of a statement
		/// </summary>
		void Semicolon()
		{
			Role role = containerStack.Peek().Role; // get the role of the current node
			if (!(role == ForStatement.InitializerRole || role == ForStatement.IteratorRole || role == UsingStatement.ResourceAcquisitionRole)) {
				WriteToken(";", AstNode.Roles.Semicolon);
				NewLine();
			}
		}
		
		/// <summary>
		/// Writes a space depending on policy.
		/// </summary>
		void Space(bool addSpace = true)
		{
			if (addSpace) {
				formatter.Space();
				lastWritten = LastWritten.Whitespace;
			}
		}
		
		void NewLine()
		{
			formatter.NewLine();
			lastWritten = LastWritten.Whitespace;
		}
		
		void OpenBrace(BraceStyle style)
		{
			WriteSpecialsUpToRole(AstNode.Roles.LBrace);
			formatter.OpenBrace(style);
			lastWritten = LastWritten.Other;
		}
		
		void CloseBrace(BraceStyle style)
		{
			WriteSpecialsUpToRole(AstNode.Roles.RBrace);
			formatter.CloseBrace(style);
			lastWritten = LastWritten.Other;
		}
		#endregion
		
		#region IsKeyword Test
		static readonly HashSet<string> unconditionalKeywords = new HashSet<string> {
			"abstract", "as", "base", "bool", "break", "byte", "case", "catch",
			"char", "checked", "class", "const", "continue", "decimal", "default", "delegate",
			"do", "double", "else", "enum", "event", "explicit", "extern", "false",
			"finally", "fixed", "float", "for", "foreach", "goto", "if", "implicit",
			"in", "int", "interface", "internal", "is", "lock", "long", "namespace",
			"new", "null", "object", "operator", "out", "override", "params", "private",
			"protected", "public", "readonly", "ref", "return", "sbyte", "sealed", "short",
			"sizeof", "stackalloc", "static", "string", "struct", "switch", "this", "throw",
			"true", "try", "typeof", "uint", "ulong", "unchecked", "unsafe", "ushort",
			"using", "virtual", "void", "volatile", "while"
		};
		
		static readonly HashSet<string> queryKeywords = new HashSet<string> {
			"from", "where", "join", "on", "equals", "into", "let", "orderby",
			"ascending", "descending", "select", "group", "by"
		};
		
		/// <summary>
		/// Determines whether the specified identifier is a keyword in the given context.
		/// </summary>
		public static bool IsKeyword(string identifier, AstNode context)
		{
			if (unconditionalKeywords.Contains(identifier))
				return true;
			if (context.Ancestors.Any(a => a is QueryExpression)) {
				if (queryKeywords.Contains(identifier))
					return true;
			}
			return false;
		}
		#endregion
		
		#region Write constructs
		void WriteTypeArguments(IEnumerable<AstType> typeArguments)
		{
			if (typeArguments.Any()) {
				WriteToken("<", AstNode.Roles.LChevron);
				WriteCommaSeparatedList(typeArguments);
				WriteToken(">", AstNode.Roles.RChevron);
			}
		}
		
		void WriteTypeParameters(IEnumerable<TypeParameterDeclaration> typeParameters)
		{
			if (typeParameters.Any()) {
				WriteToken("<", AstNode.Roles.LChevron);
				WriteCommaSeparatedList(typeParameters.SafeCast<TypeParameterDeclaration, AstNode>());
				WriteToken(">", AstNode.Roles.RChevron);
			}
		}
		
		void WriteModifiers(IEnumerable<CSharpModifierToken> modifierTokens)
		{
			foreach (CSharpModifierToken modifier in modifierTokens) {
				modifier.AcceptVisitor(this, null);
			}
		}
		
		void WriteQualifiedIdentifier(IEnumerable<Identifier> identifiers)
		{
			bool first = true;
			foreach (Identifier ident in identifiers) {
				if (first) {
					first = false;
					if (lastWritten == LastWritten.KeywordOrIdentifier)
						formatter.Space();
				} else {
					WriteSpecialsUpToRole(AstNode.Roles.Dot, ident);
					formatter.WriteToken(".");
					lastWritten = LastWritten.Other;
				}
				WriteSpecialsUpToNode(ident);
				formatter.WriteIdentifier(ident.Name);
				lastWritten = LastWritten.KeywordOrIdentifier;
			}
		}
		
		void WriteEmbeddedStatement(Statement embeddedStatement)
		{
			if (embeddedStatement.IsNull)
				return;
			BlockStatement block = embeddedStatement as BlockStatement;
			if (block != null)
				VisitBlockStatement(block, null);
			else
				throw new NotImplementedException();
		}
		
		void WriteMethodBody(BlockStatement body)
		{
			if (body.IsNull)
				Semicolon();
			else
				VisitBlockStatement(body, null);
		}
		
		void WriteAttributes(IEnumerable<AttributeSection> attributes)
		{
			foreach (AttributeSection attr in attributes) {
				attr.AcceptVisitor(this, null);
			}
		}
		
		void WritePrivateImplementationType(AstType privateImplementationType)
		{
			if (!privateImplementationType.IsNull) {
				privateImplementationType.AcceptVisitor(this, null);
				WriteToken(".", AstNode.Roles.Dot);
			}
		}
		#endregion
		
		#region Expressions
		public object VisitAnonymousMethodExpression(AnonymousMethodExpression anonymousMethodExpression, object data)
		{
			StartNode(anonymousMethodExpression);
			WriteKeyword("delegate");
			if (anonymousMethodExpression.HasParameterList) {
				Space(policy.BeforeMethodDeclarationParentheses);
				WriteCommaSeparatedListInParenthesis(anonymousMethodExpression.Parameters, policy.WithinMethodDeclarationParentheses);
			}
			anonymousMethodExpression.Body.AcceptVisitor(this, data);
			return EndNode(anonymousMethodExpression);
		}
		
		public object VisitArgListExpression(ArgListExpression argListExpression, object data)
		{
			StartNode(argListExpression);
			WriteKeyword("__arglist");
			if (!argListExpression.IsAccess) {
				Space(policy.BeforeMethodCallParentheses);
				WriteCommaSeparatedListInParenthesis(argListExpression.Arguments, policy.WithinMethodCallParentheses);
			}
			return EndNode(argListExpression);
		}
		
		public object VisitArrayCreateExpression(ArrayCreateExpression arrayCreateExpression, object data)
		{
			StartNode(arrayCreateExpression);
			WriteKeyword("new");
			arrayCreateExpression.Type.AcceptVisitor(this, data);
			WriteCommaSeparatedListInBrackets(arrayCreateExpression.Arguments);
			foreach (var specifier in arrayCreateExpression.AdditionalArraySpecifiers)
				specifier.AcceptVisitor(this, data);
			arrayCreateExpression.Initializer.AcceptVisitor(this, data);
			return EndNode(arrayCreateExpression);
		}
		
		public object VisitArrayInitializerExpression(ArrayInitializerExpression arrayInitializerExpression, object data)
		{
			StartNode(arrayInitializerExpression);
			BraceStyle style;
			if (policy.PlaceArrayInitializersOnNewLine == ArrayInitializerPlacement.AlwaysNewLine)
				style = BraceStyle.NextLine;
			else
				style = BraceStyle.EndOfLine;
			OpenBrace(style);
			bool isFirst = true;
			foreach (AstNode node in arrayInitializerExpression.Children) {
				if (isFirst) {
					isFirst = false;
				} else {
					Comma(node);
					NewLine();
				}
				node.AcceptVisitor(this, null);
			}
			NewLine();
			CloseBrace(style);
			return EndNode(arrayInitializerExpression);
		}
		
		public object VisitAsExpression(AsExpression asExpression, object data)
		{
			StartNode(asExpression);
			asExpression.Expression.AcceptVisitor(this, data);
			Space();
			WriteKeyword("as");
			Space();
			asExpression.Type.AcceptVisitor(this, data);
			return EndNode(asExpression);
		}
		
		public object VisitAssignmentExpression(AssignmentExpression assignmentExpression, object data)
		{
			StartNode(assignmentExpression);
			assignmentExpression.Left.AcceptVisitor(this, data);
			Space(policy.AroundAssignmentParentheses);
			WriteToken(AssignmentExpression.GetOperatorSymbol(assignmentExpression.Operator), AssignmentExpression.OperatorRole);
			Space(policy.AroundAssignmentParentheses);
			assignmentExpression.Right.AcceptVisitor(this, data);
			return EndNode(assignmentExpression);
		}
		
		public object VisitBaseReferenceExpression(BaseReferenceExpression baseReferenceExpression, object data)
		{
			StartNode(baseReferenceExpression);
			WriteKeyword("base");
			return EndNode(baseReferenceExpression);
		}
		
		public object VisitBinaryOperatorExpression(BinaryOperatorExpression binaryOperatorExpression, object data)
		{
			StartNode(binaryOperatorExpression);
			binaryOperatorExpression.Left.AcceptVisitor(this, data);
			bool spacePolicy;
			switch (binaryOperatorExpression.Operator) {
				case BinaryOperatorType.BitwiseAnd:
				case BinaryOperatorType.BitwiseOr:
				case BinaryOperatorType.ExclusiveOr:
					spacePolicy = policy.AroundBitwiseOperatorParentheses;
					break;
				case BinaryOperatorType.ConditionalAnd:
				case BinaryOperatorType.ConditionalOr:
					spacePolicy = policy.AroundLogicalOperatorParentheses;
					break;
				case BinaryOperatorType.GreaterThan:
				case BinaryOperatorType.GreaterThanOrEqual:
				case BinaryOperatorType.LessThanOrEqual:
				case BinaryOperatorType.LessThan:
					spacePolicy = policy.AroundRelationalOperatorParentheses;
					break;
				case BinaryOperatorType.Equality:
				case BinaryOperatorType.InEquality:
					spacePolicy = policy.AroundEqualityOperatorParentheses;
					break;
				case BinaryOperatorType.Add:
				case BinaryOperatorType.Subtract:
					spacePolicy = policy.AroundAdditiveOperatorParentheses;
					break;
				case BinaryOperatorType.Multiply:
				case BinaryOperatorType.Divide:
				case BinaryOperatorType.Modulus:
					spacePolicy = policy.AroundMultiplicativeOperatorParentheses;
					break;
				case BinaryOperatorType.ShiftLeft:
				case BinaryOperatorType.ShiftRight:
					spacePolicy = policy.AroundShiftOperatorParentheses;
					break;
				case BinaryOperatorType.NullCoalescing:
					spacePolicy = true;
					break;
				default:
					throw new NotSupportedException("Invalid value for BinaryOperatorType");
			}
			Space(spacePolicy);
			WriteToken(BinaryOperatorExpression.GetOperatorSymbol(binaryOperatorExpression.Operator), BinaryOperatorExpression.OperatorRole);
			Space(spacePolicy);
			binaryOperatorExpression.Right.AcceptVisitor(this, data);
			return EndNode(binaryOperatorExpression);
		}
		
		public object VisitCastExpression(CastExpression castExpression, object data)
		{
			StartNode(castExpression);
			LPar();
			Space(policy.WithinCastParentheses);
			castExpression.Type.AcceptVisitor(this, data);
			Space(policy.WithinCastParentheses);
			RPar();
			Space(policy.SpacesAfterTypecast);
			castExpression.Expression.AcceptVisitor(this, data);
			return EndNode(castExpression);
		}
		
		public object VisitCheckedExpression(CheckedExpression checkedExpression, object data)
		{
			StartNode(checkedExpression);
			WriteKeyword("checked");
			LPar();
			Space(policy.WithinCheckedExpressionParantheses);
			checkedExpression.Expression.AcceptVisitor(this, data);
			Space(policy.WithinCheckedExpressionParantheses);
			RPar();
			return EndNode(checkedExpression);
		}
		
		public object VisitConditionalExpression(ConditionalExpression conditionalExpression, object data)
		{
			StartNode(conditionalExpression);
			conditionalExpression.Condition.AcceptVisitor(this, data);
			
			Space(policy.ConditionalOperatorBeforeConditionSpace);
			WriteToken("?", ConditionalExpression.QuestionMarkRole);
			Space(policy.ConditionalOperatorAfterConditionSpace);
			
			conditionalExpression.TrueExpression.AcceptVisitor(this, data);
			
			Space(policy.ConditionalOperatorBeforeSeparatorSpace);
			WriteToken(":", ConditionalExpression.ColonRole);
			Space(policy.ConditionalOperatorAfterSeparatorSpace);
			
			conditionalExpression.FalseExpression.AcceptVisitor(this, data);
			
			return EndNode(conditionalExpression);
		}
		
		public object VisitDefaultValueExpression(DefaultValueExpression defaultValueExpression, object data)
		{
			StartNode(defaultValueExpression);
			
			WriteKeyword("default");
			LPar();
			Space(policy.WithinTypeOfParentheses);
			defaultValueExpression.Type.AcceptVisitor(this, data);
			Space(policy.WithinTypeOfParentheses);
			RPar();
			
			return EndNode(defaultValueExpression);
		}
		
		public object VisitDirectionExpression(DirectionExpression directionExpression, object data)
		{
			StartNode(directionExpression);
			
			switch (directionExpression.FieldDirection) {
				case FieldDirection.Out:
					WriteKeyword("out");
					break;
				case FieldDirection.Ref:
					WriteKeyword("ref");
					break;
				default:
					throw new NotSupportedException("Invalid value for FieldDirection");
			}
			Space();
			directionExpression.Expression.AcceptVisitor(this, data);
			
			return EndNode(directionExpression);
		}
		
		public object VisitIdentifierExpression(IdentifierExpression identifierExpression, object data)
		{
			StartNode(identifierExpression);
			WriteIdentifier(identifierExpression.Identifier);
			WriteTypeArguments(identifierExpression.TypeArguments);
			return EndNode(identifierExpression);
		}
		
		public object VisitIndexerExpression(IndexerExpression indexerExpression, object data)
		{
			StartNode(indexerExpression);
			indexerExpression.Target.AcceptVisitor(this, data);
			Space(policy.BeforeMethodCallParentheses);
			WriteCommaSeparatedListInBrackets(indexerExpression.Arguments);
			return EndNode(indexerExpression);
		}
		
		public object VisitInvocationExpression(InvocationExpression invocationExpression, object data)
		{
			StartNode(invocationExpression);
			invocationExpression.Target.AcceptVisitor(this, data);
			Space(policy.BeforeMethodCallParentheses);
			WriteCommaSeparatedListInParenthesis(invocationExpression.Arguments, policy.WithinMethodCallParentheses);
			return EndNode(invocationExpression);
		}
		
		public object VisitIsExpression(IsExpression isExpression, object data)
		{
			StartNode(isExpression);
			isExpression.Expression.AcceptVisitor(this, data);
			Space();
			WriteKeyword("is");
			isExpression.Type.AcceptVisitor(this, data);
			return EndNode(isExpression);
		}
		
		public object VisitLambdaExpression(LambdaExpression lambdaExpression, object data)
		{
			StartNode(lambdaExpression);
			if (LambdaNeedsParenthesis(lambdaExpression)) {
				WriteCommaSeparatedListInParenthesis(lambdaExpression.Parameters, policy.WithinMethodDeclarationParentheses);
			} else {
				lambdaExpression.Parameters.Single().AcceptVisitor(this, data);
			}
			Space();
			WriteToken("=>", LambdaExpression.ArrowRole);
			Space();
			lambdaExpression.Body.AcceptVisitor(this, data);
			return EndNode(lambdaExpression);
		}
		
		bool LambdaNeedsParenthesis(LambdaExpression lambdaExpression)
		{
			if (lambdaExpression.Parameters.Count != 1)
				return true;
			var p = lambdaExpression.Parameters.Single();
			return !(p.Type.IsNull && p.ParameterModifier == ParameterModifier.None);
		}
		
		public object VisitMemberReferenceExpression(MemberReferenceExpression memberReferenceExpression, object data)
		{
			StartNode(memberReferenceExpression);
			memberReferenceExpression.Target.AcceptVisitor(this, data);
			WriteToken(".", MemberReferenceExpression.Roles.Dot);
			WriteIdentifier(memberReferenceExpression.MemberName);
			WriteTypeArguments(memberReferenceExpression.TypeArguments);
			return EndNode(memberReferenceExpression);
		}
		
		public object VisitNamedArgumentExpression(NamedArgumentExpression namedArgumentExpression, object data)
		{
			StartNode(namedArgumentExpression);
			WriteIdentifier(namedArgumentExpression.Identifier);
			WriteToken(":", NamedArgumentExpression.Roles.Colon);
			Space();
			namedArgumentExpression.Expression.AcceptVisitor(this, data);
			return EndNode(namedArgumentExpression);
		}
		
		public object VisitNullReferenceExpression(NullReferenceExpression nullReferenceExpression, object data)
		{
			StartNode(nullReferenceExpression);
			WriteKeyword("null");
			return EndNode(nullReferenceExpression);
		}
		
		public object VisitObjectCreateExpression(ObjectCreateExpression objectCreateExpression, object data)
		{
			StartNode(objectCreateExpression);
			WriteKeyword("new");
			objectCreateExpression.Type.AcceptVisitor(this, data);
			Space(policy.BeforeMethodCallParentheses);
			WriteCommaSeparatedListInParenthesis(objectCreateExpression.Arguments, policy.WithinMethodCallParentheses);
			objectCreateExpression.Initializer.AcceptVisitor(this, data);
			return EndNode(objectCreateExpression);
		}
		
		public object VisitParenthesizedExpression(ParenthesizedExpression parenthesizedExpression, object data)
		{
			StartNode(parenthesizedExpression);
			LPar();
			Space(policy.WithinParentheses);
			parenthesizedExpression.Expression.AcceptVisitor(this, data);
			Space(policy.WithinParentheses);
			RPar();
			return EndNode(parenthesizedExpression);
		}
		
		public object VisitPointerReferenceExpression(PointerReferenceExpression pointerReferenceExpression, object data)
		{
			StartNode(pointerReferenceExpression);
			pointerReferenceExpression.Target.AcceptVisitor(this, data);
			WriteToken("->", PointerReferenceExpression.ArrowRole);
			WriteIdentifier(pointerReferenceExpression.MemberName);
			WriteTypeArguments(pointerReferenceExpression.TypeArguments);
			return EndNode(pointerReferenceExpression);
		}
		
		#region VisitPrimitiveExpression
		public object VisitPrimitiveExpression(PrimitiveExpression primitiveExpression, object data)
		{
			StartNode(primitiveExpression);
			WritePrimitiveValue(primitiveExpression.Value);
			return EndNode(primitiveExpression);
		}
		
		void WritePrimitiveValue(object val)
		{
			if (val == null) {
				// usually NullReferenceExpression should be used for this, but we'll handle it anyways
				WriteKeyword("null");
				return;
			}
			
			if (val is bool) {
				if ((bool)val) {
					WriteKeyword("true");
				} else {
					WriteKeyword("false");
				}
				return;
			}
			
			if (val is string) {
				formatter.WriteToken("\"" + ConvertString(val.ToString()) + "\"");
				lastWritten = LastWritten.Other;
			} else if (val is char) {
				formatter.WriteToken("'" + ConvertCharLiteral((char)val) + "'");
				lastWritten = LastWritten.Other;
			} else if (val is decimal) {
				formatter.WriteToken(((decimal)val).ToString(NumberFormatInfo.InvariantInfo) + "m");
				lastWritten = LastWritten.Other;
			} else if (val is float) {
				float f = (float)val;
				if (float.IsInfinity(f) || float.IsNaN(f)) {
					// Strictly speaking, these aren't PrimitiveExpressions;
					// but we still support writing these to make life easier for code generators.
					WriteKeyword("float");
					WriteToken(".", AstNode.Roles.Dot);
					if (float.IsPositiveInfinity(f))
						WriteIdentifier("PositiveInfinity");
					else if (float.IsNegativeInfinity(f))
						WriteIdentifier("NegativeInfinity");
					else
						WriteIdentifier("NaN");
					return;
				}
				formatter.WriteToken(f.ToString("R", NumberFormatInfo.InvariantInfo) + "f");
				lastWritten = LastWritten.Other;
			} else if (val is double) {
				double f = (double)val;
				if (double.IsInfinity(f) || double.IsNaN(f)) {
					// Strictly speaking, these aren't PrimitiveExpressions;
					// but we still support writing these to make life easier for code generators.
					WriteKeyword("double");
					WriteToken(".", AstNode.Roles.Dot);
					if (double.IsPositiveInfinity(f))
						WriteIdentifier("PositiveInfinity");
					else if (double.IsNegativeInfinity(f))
						WriteIdentifier("NegativeInfinity");
					else
						WriteIdentifier("NaN");
					return;
				}
				string number = f.ToString("R", NumberFormatInfo.InvariantInfo);
				if (number.IndexOf('.') < 0 && number.IndexOf('E') < 0)
					number += ".0";
				formatter.WriteToken(number);
				// needs space if identifier follows number; this avoids mistaking the following identifier as type suffix
				lastWritten = LastWritten.KeywordOrIdentifier;
			} else if (val is IFormattable) {
				StringBuilder b = new StringBuilder();
//				if (primitiveExpression.LiteralFormat == LiteralFormat.HexadecimalNumber) {
//					b.Append("0x");
//					b.Append(((IFormattable)val).ToString("x", NumberFormatInfo.InvariantInfo));
//				} else {
				b.Append(((IFormattable)val).ToString(null, NumberFormatInfo.InvariantInfo));
//				}
				if (val is uint || val is ulong) {
					b.Append("u");
				}
				if (val is long || val is ulong) {
					b.Append("L");
				}
				formatter.WriteToken(b.ToString());
				// needs space if identifier follows number; this avoids mistaking the following identifier as type suffix
				lastWritten = LastWritten.KeywordOrIdentifier;
			} else {
				formatter.WriteToken(val.ToString());
				lastWritten = LastWritten.Other;
			}
		}
		
		static string ConvertCharLiteral(char ch)
		{
			if (ch == '\'') return "\\'";
			return ConvertChar(ch);
		}
		
		static string ConvertChar(char ch)
		{
			switch (ch) {
				case '\\':
					return "\\\\";
				case '\0':
					return "\\0";
				case '\a':
					return "\\a";
				case '\b':
					return "\\b";
				case '\f':
					return "\\f";
				case '\n':
					return "\\n";
				case '\r':
					return "\\r";
				case '\t':
					return "\\t";
				case '\v':
					return "\\v";
				default:
					if (char.IsControl(ch) || char.IsSurrogate(ch)) {
						return "\\u" + ((int)ch).ToString("x4");
					} else {
						return ch.ToString();
					}
			}
		}
		
		static string ConvertString(string str)
		{
			StringBuilder sb = new StringBuilder();
			foreach (char ch in str) {
				if (ch == '"')
					sb.Append("\\\"");
				else
					sb.Append(ConvertChar(ch));
			}
			return sb.ToString();
		}
		#endregion
		
		public object VisitSizeOfExpression(SizeOfExpression sizeOfExpression, object data)
		{
			StartNode(sizeOfExpression);
			
			WriteKeyword("sizeof");
			LPar();
			Space(policy.WithinSizeOfParentheses);
			sizeOfExpression.Type.AcceptVisitor(this, data);
			Space(policy.WithinSizeOfParentheses);
			RPar();
			
			return EndNode(sizeOfExpression);
		}
		
		public object VisitStackAllocExpression(StackAllocExpression stackAllocExpression, object data)
		{
			StartNode(stackAllocExpression);
			WriteKeyword("stackalloc");
			stackAllocExpression.Type.AcceptVisitor(this, data);
			WriteCommaSeparatedListInBrackets(new[] { stackAllocExpression.CountExpression });
			return EndNode(stackAllocExpression);
		}
		
		public object VisitThisReferenceExpression(ThisReferenceExpression thisReferenceExpression, object data)
		{
			StartNode(thisReferenceExpression);
			WriteKeyword("this");
			return EndNode(thisReferenceExpression);
		}
		
		public object VisitTypeOfExpression(TypeOfExpression typeOfExpression, object data)
		{
			StartNode(typeOfExpression);
			
			WriteKeyword("typeof");
			LPar();
			Space(policy.WithinTypeOfParentheses);
			typeOfExpression.Type.AcceptVisitor(this, data);
			Space(policy.WithinTypeOfParentheses);
			RPar();
			
			return EndNode(typeOfExpression);
		}
		
		public object VisitTypeReferenceExpression(TypeReferenceExpression typeReferenceExpression, object data)
		{
			StartNode(typeReferenceExpression);
			typeReferenceExpression.Type.AcceptVisitor(this, data);
			return EndNode(typeReferenceExpression);
		}
		
		public object VisitUnaryOperatorExpression(UnaryOperatorExpression unaryOperatorExpression, object data)
		{
			StartNode(unaryOperatorExpression);
			UnaryOperatorType opType = unaryOperatorExpression.Operator;
			string opSymbol = UnaryOperatorExpression.GetOperatorSymbol(opType);
			if (!(opType == UnaryOperatorType.PostIncrement || opType == UnaryOperatorType.PostDecrement))
				WriteToken(opSymbol, UnaryOperatorExpression.OperatorRole);
			unaryOperatorExpression.Expression.AcceptVisitor(this, data);
			if (opType == UnaryOperatorType.PostIncrement || opType == UnaryOperatorType.PostDecrement)
				WriteToken(opSymbol, UnaryOperatorExpression.OperatorRole);
			return EndNode(unaryOperatorExpression);
		}
		
		public object VisitUncheckedExpression(UncheckedExpression uncheckedExpression, object data)
		{
			StartNode(uncheckedExpression);
			WriteKeyword("unchecked");
			LPar();
			Space(policy.WithinCheckedExpressionParantheses);
			uncheckedExpression.Expression.AcceptVisitor(this, data);
			Space(policy.WithinCheckedExpressionParantheses);
			RPar();
			return EndNode(uncheckedExpression);
		}
		#endregion
		
		#region Query Expressions
		public object VisitQueryExpression(QueryExpression queryExpression, object data)
		{
			StartNode(queryExpression);
			bool first = true;
			foreach (var clause in queryExpression.Clauses) {
				if (first) {
					first = false;
				} else {
					NewLine();
				}
				clause.AcceptVisitor(this, data);
			}
			return EndNode(queryExpression);
		}
		
		public object VisitQueryContinuationClause(QueryContinuationClause queryContinuationClause, object data)
		{
			StartNode(queryContinuationClause);
			queryContinuationClause.PrecedingQuery.AcceptVisitor(this, data);
			Space();
			WriteKeyword("into", QueryContinuationClause.IntoKeywordRole);
			Space();
			WriteIdentifier(queryContinuationClause.Identifier);
			return EndNode(queryContinuationClause);
		}
		
		public object VisitQueryFromClause(QueryFromClause queryFromClause, object data)
		{
			StartNode(queryFromClause);
			WriteKeyword("from", QueryFromClause.FromKeywordRole);
			queryFromClause.Type.AcceptVisitor(this, data);
			Space();
			WriteIdentifier(queryFromClause.Identifier);
			WriteKeyword("in", QueryFromClause.InKeywordRole);
			queryFromClause.Expression.AcceptVisitor(this, data);
			return EndNode(queryFromClause);
		}
		
		public object VisitQueryLetClause(QueryLetClause queryLetClause, object data)
		{
			StartNode(queryLetClause);
			WriteKeyword("let");
			Space();
			WriteIdentifier(queryLetClause.Identifier);
			Space(policy.AroundAssignmentParentheses);
			WriteToken("=", QueryLetClause.Roles.Assign);
			Space(policy.AroundAssignmentParentheses);
			queryLetClause.Expression.AcceptVisitor(this, data);
			return EndNode(queryLetClause);
		}
		
		public object VisitQueryWhereClause(QueryWhereClause queryWhereClause, object data)
		{
			StartNode(queryWhereClause);
			WriteKeyword("where");
			Space();
			queryWhereClause.Condition.AcceptVisitor(this, data);
			return EndNode(queryWhereClause);
		}
		
		public object VisitQueryJoinClause(QueryJoinClause queryJoinClause, object data)
		{
			StartNode(queryJoinClause);
			WriteKeyword("join", QueryJoinClause.JoinKeywordRole);
			queryJoinClause.Type.AcceptVisitor(this, data);
			Space();
			WriteIdentifier(queryJoinClause.JoinIdentifier, QueryJoinClause.JoinIdentifierRole);
			Space();
			WriteKeyword("in", QueryJoinClause.InKeywordRole);
			Space();
			queryJoinClause.InExpression.AcceptVisitor(this, data);
			Space();
			WriteKeyword("on", QueryJoinClause.OnKeywordRole);
			Space();
			queryJoinClause.OnExpression.AcceptVisitor(this, data);
			Space();
			WriteKeyword("equals", QueryJoinClause.EqualsKeywordRole);
			Space();
			queryJoinClause.EqualsExpression.AcceptVisitor(this, data);
			if (queryJoinClause.IsGroupJoin) {
				Space();
				WriteKeyword("into", QueryJoinClause.IntoKeywordRole);
				WriteIdentifier(queryJoinClause.IntoIdentifier, QueryJoinClause.IntoIdentifierRole);
			}
			return EndNode(queryJoinClause);
		}
		
		public object VisitQueryOrderClause(QueryOrderClause queryOrderClause, object data)
		{
			StartNode(queryOrderClause);
			WriteKeyword("orderby");
			Space();
			WriteCommaSeparatedList(queryOrderClause.Orderings.SafeCast<QueryOrdering, AstNode>());
			return EndNode(queryOrderClause);
		}
		
		public object VisitQueryOrdering(QueryOrdering queryOrdering, object data)
		{
			StartNode(queryOrdering);
			queryOrdering.Expression.AcceptVisitor(this, data);
			switch (queryOrdering.Direction) {
				case QueryOrderingDirection.Ascending:
					Space();
					WriteKeyword("ascending");
					break;
				case QueryOrderingDirection.Descending:
					Space();
					WriteKeyword("descending");
					break;
			}
			return EndNode(queryOrdering);
		}
		
		public object VisitQuerySelectClause(QuerySelectClause querySelectClause, object data)
		{
			StartNode(querySelectClause);
			WriteKeyword("select");
			Space();
			querySelectClause.Expression.AcceptVisitor(this, data);
			return EndNode(querySelectClause);
		}
		
		public object VisitQueryGroupClause(QueryGroupClause queryGroupClause, object data)
		{
			StartNode(queryGroupClause);
			WriteKeyword("group", QueryGroupClause.GroupKeywordRole);
			Space();
			queryGroupClause.Projection.AcceptVisitor(this, data);
			Space();
			WriteKeyword("by", QueryGroupClause.ByKeywordRole);
			Space();
			queryGroupClause.Key.AcceptVisitor(this, data);
			return EndNode(queryGroupClause);
		}
		#endregion
		
		#region GeneralScope
		public object VisitAttribute(Attribute attribute, object data)
		{
			StartNode(attribute);
			attribute.Type.AcceptVisitor(this, data);
			Space(policy.BeforeMethodCallParentheses);
			if (attribute.Arguments.Count != 0 || !attribute.GetChildByRole(AstNode.Roles.LPar).IsNull)
				WriteCommaSeparatedListInParenthesis(attribute.Arguments, policy.WithinMethodCallParentheses);
			return EndNode(attribute);
		}
		
		public object VisitAttributeSection(AttributeSection attributeSection, object data)
		{
			StartNode(attributeSection);
			WriteToken("[", AstNode.Roles.LBracket);
			if (attributeSection.AttributeTarget != AttributeTarget.None) {
				WriteToken(AttributeSection.GetAttributeTargetName(attributeSection.AttributeTarget), AttributeSection.TargetRole);
				WriteToken(":", AttributeSection.Roles.Colon);
				Space();
			}
			WriteCommaSeparatedList(attributeSection.Attributes.SafeCast<Attribute, AstNode>());
			WriteToken("]", AstNode.Roles.RBracket);
			if (attributeSection.Parent is ParameterDeclaration || attributeSection.Parent is TypeParameterDeclaration)
				Space();
			else
				NewLine();
			return EndNode(attributeSection);
		}
		
		public object VisitDelegateDeclaration(DelegateDeclaration delegateDeclaration, object data)
		{
			StartNode(delegateDeclaration);
			WriteAttributes(delegateDeclaration.Attributes);
			WriteModifiers(delegateDeclaration.ModifierTokens);
			WriteKeyword("delegate");
			delegateDeclaration.ReturnType.AcceptVisitor(this, data);
			Space();
			WriteIdentifier(delegateDeclaration.Name);
			WriteTypeParameters(delegateDeclaration.TypeParameters);
			Space(policy.BeforeDelegateDeclarationParentheses);
			WriteCommaSeparatedListInParenthesis(delegateDeclaration.Parameters, policy.WithinMethodDeclarationParentheses);
			foreach (Constraint constraint in delegateDeclaration.Constraints) {
				constraint.AcceptVisitor(this, data);
			}
			Semicolon();
			return EndNode(delegateDeclaration);
		}
		
		public object VisitNamespaceDeclaration(NamespaceDeclaration namespaceDeclaration, object data)
		{
			StartNode(namespaceDeclaration);
			WriteKeyword("namespace");
			WriteQualifiedIdentifier(namespaceDeclaration.Identifiers);
			OpenBrace(policy.NamespaceBraceStyle);
			foreach (var member in namespaceDeclaration.Members)
				member.AcceptVisitor(this, data);
			CloseBrace(policy.NamespaceBraceStyle);
			NewLine();
			return EndNode(namespaceDeclaration);
		}
		
		public object VisitTypeDeclaration(TypeDeclaration typeDeclaration, object data)
		{
			StartNode(typeDeclaration);
			WriteAttributes(typeDeclaration.Attributes);
			WriteModifiers(typeDeclaration.ModifierTokens);
			BraceStyle braceStyle;
			switch (typeDeclaration.ClassType) {
				case ClassType.Enum:
					WriteKeyword("enum");
					braceStyle = policy.EnumBraceStyle;
					break;
				case ClassType.Interface:
					WriteKeyword("interface");
					braceStyle = policy.InterfaceBraceStyle;
					break;
				case ClassType.Struct:
					WriteKeyword("struct");
					braceStyle = policy.StructBraceStyle;
					break;
				default:
					WriteKeyword("class");
					braceStyle = policy.ClassBraceStyle;
					break;
			}
			WriteIdentifier(typeDeclaration.Name);
			WriteTypeParameters(typeDeclaration.TypeParameters);
			if (typeDeclaration.BaseTypes.Any()) {
				Space();
				WriteToken(":", TypeDeclaration.ColonRole);
				Space();
				WriteCommaSeparatedList(typeDeclaration.BaseTypes);
			}
			foreach (Constraint constraint in typeDeclaration.Constraints) {
				constraint.AcceptVisitor(this, data);
			}
			OpenBrace(braceStyle);
			if (typeDeclaration.ClassType == ClassType.Enum) {
				bool first = true;
				foreach (var member in typeDeclaration.Members) {
					if (first) {
						first = false;
					} else {
						Comma(member, noSpaceAfterComma: true);
						NewLine();
					}
					member.AcceptVisitor(this, data);
				}
				NewLine();
			} else {
				foreach (var member in typeDeclaration.Members) {
					member.AcceptVisitor(this, data);
				}
			}
			CloseBrace(braceStyle);
			NewLine();
			return EndNode(typeDeclaration);
		}
		
		public object VisitUsingAliasDeclaration(UsingAliasDeclaration usingAliasDeclaration, object data)
		{
			StartNode(usingAliasDeclaration);
			WriteKeyword("using");
			WriteIdentifier(usingAliasDeclaration.Alias, UsingAliasDeclaration.AliasRole);
			Space(policy.AroundEqualityOperatorParentheses);
			WriteToken("=", AstNode.Roles.Assign);
			Space(policy.AroundEqualityOperatorParentheses);
			usingAliasDeclaration.Import.AcceptVisitor(this, data);
			Semicolon();
			return EndNode(usingAliasDeclaration);
		}
		
		public object VisitUsingDeclaration(UsingDeclaration usingDeclaration, object data)
		{
			StartNode(usingDeclaration);
			WriteKeyword("using");
			usingDeclaration.Import.AcceptVisitor(this, data);
			Semicolon();
			return EndNode(usingDeclaration);
		}
		#endregion
		
		#region Statements
		public object VisitBlockStatement(BlockStatement blockStatement, object data)
		{
			StartNode(blockStatement);
			BraceStyle style;
			if (blockStatement.Parent is AnonymousMethodExpression || blockStatement.Parent is LambdaExpression) {
				style = policy.AnonymousMethodBraceStyle;
			} else if (blockStatement.Parent is ConstructorDeclaration) {
				style = policy.ConstructorBraceStyle;
			} else if (blockStatement.Parent is DestructorDeclaration) {
				style = policy.DestructorBraceStyle;
			} else if (blockStatement.Parent is MethodDeclaration) {
				style = policy.MethodBraceStyle;
			} else if (blockStatement.Parent is Accessor) {
				if (blockStatement.Parent.Role == PropertyDeclaration.GetterRole)
					style = policy.PropertyGetBraceStyle;
				else if (blockStatement.Parent.Role == PropertyDeclaration.SetterRole)
					style = policy.PropertySetBraceStyle;
				else if (blockStatement.Parent.Role == CustomEventDeclaration.AddAccessorRole)
					style = policy.EventAddBraceStyle;
				else if (blockStatement.Parent.Role == CustomEventDeclaration.RemoveAccessorRole)
					style = policy.EventRemoveBraceStyle;
				else
					throw new NotSupportedException("Unknown type of accessor");
			} else {
				style = policy.StatementBraceStyle;
			}
			OpenBrace(style);
			foreach (var node in blockStatement.Statements) {
				node.AcceptVisitor(this, data);
			}
			CloseBrace(style);
			NewLine();
			return EndNode(blockStatement);
		}
		
		public object VisitBreakStatement(BreakStatement breakStatement, object data)
		{
			StartNode(breakStatement);
			WriteKeyword("break");
			Semicolon();
			return EndNode(breakStatement);
		}
		
		public object VisitCheckedStatement(CheckedStatement checkedStatement, object data)
		{
			StartNode(checkedStatement);
			WriteKeyword("checked");
			checkedStatement.Body.AcceptVisitor(this, data);
			return EndNode(checkedStatement);
		}
		
		public object VisitContinueStatement(ContinueStatement continueStatement, object data)
		{
			StartNode(continueStatement);
			WriteKeyword("continue");
			Semicolon();
			return EndNode(continueStatement);
		}
		
		public object VisitDoWhileStatement(DoWhileStatement doWhileStatement, object data)
		{
			StartNode(doWhileStatement);
			WriteKeyword("do", DoWhileStatement.DoKeywordRole);
			WriteEmbeddedStatement(doWhileStatement.EmbeddedStatement);
			WriteKeyword("while", DoWhileStatement.WhileKeywordRole);
			Space(policy.WhileParentheses);
			LPar();
			Space(policy.WithinWhileParentheses);
			doWhileStatement.Condition.AcceptVisitor(this, data);
			Space(policy.WithinWhileParentheses);
			RPar();
			Semicolon();
			return EndNode(doWhileStatement);
		}
		
		public object VisitEmptyStatement(EmptyStatement emptyStatement, object data)
		{
			StartNode(emptyStatement);
			Semicolon();
			return EndNode(emptyStatement);
		}
		
		public object VisitExpressionStatement(ExpressionStatement expressionStatement, object data)
		{
			StartNode(expressionStatement);
			expressionStatement.Expression.AcceptVisitor(this, data);
			Semicolon();
			return EndNode(expressionStatement);
		}
		
		public object VisitFixedStatement(FixedStatement fixedStatement, object data)
		{
			StartNode(fixedStatement);
			WriteKeyword("fixed");
			LPar();
			fixedStatement.Type.AcceptVisitor(this, data);
			WriteCommaSeparatedList(fixedStatement.Variables);
			RPar();
			WriteEmbeddedStatement(fixedStatement.EmbeddedStatement);
			return EndNode(fixedStatement);
		}
		
		public object VisitForeachStatement(ForeachStatement foreachStatement, object data)
		{
			StartNode(foreachStatement);
			WriteKeyword("foreach");
			Space(policy.ForeachParentheses);
			LPar();
			Space(policy.WithinForEachParentheses);
			foreachStatement.VariableType.AcceptVisitor(this, data);
			Space();
			WriteIdentifier(foreachStatement.VariableName);
			WriteKeyword("in", ForeachStatement.Roles.InKeyword);
			Space();
			foreachStatement.InExpression.AcceptVisitor(this, data);
			Space(policy.WithinForEachParentheses);
			RPar();
			WriteEmbeddedStatement(foreachStatement.EmbeddedStatement);
			return EndNode(foreachStatement);
		}
		
		public object VisitForStatement(ForStatement forStatement, object data)
		{
			StartNode(forStatement);
			WriteKeyword("for");
			Space(policy.ForParentheses);
			LPar();
			Space(policy.WithinForParentheses);
			
			WriteCommaSeparatedList(forStatement.Initializers.SafeCast<Statement, AstNode>());
			WriteToken(";", AstNode.Roles.Semicolon);
			Space(policy.SpacesAfterSemicolon);
			
			forStatement.Condition.AcceptVisitor(this, data);
			WriteToken(";", AstNode.Roles.Semicolon);
			Space(policy.SpacesAfterSemicolon);
			
			WriteCommaSeparatedList(forStatement.Iterators.SafeCast<Statement, AstNode>());
			
			Space(policy.WithinForParentheses);
			RPar();
			WriteEmbeddedStatement(forStatement.EmbeddedStatement);
			return EndNode(forStatement);
		}
		
		public object VisitGotoCaseStatement(GotoCaseStatement gotoCaseStatement, object data)
		{
			StartNode(gotoCaseStatement);
			WriteKeyword("goto");
			WriteKeyword("case", GotoCaseStatement.CaseKeywordRole);
			Space();
			gotoCaseStatement.LabelExpression.AcceptVisitor(this, data);
			Semicolon();
			return EndNode(gotoCaseStatement);
		}
		
		public object VisitGotoDefaultStatement(GotoDefaultStatement gotoDefaultStatement, object data)
		{
			StartNode(gotoDefaultStatement);
			WriteKeyword("goto");
			WriteKeyword("default", GotoDefaultStatement.DefaultKeywordRole);
			Semicolon();
			return EndNode(gotoDefaultStatement);
		}
		
		public object VisitGotoStatement(GotoStatement gotoStatement, object data)
		{
			StartNode(gotoStatement);
			WriteKeyword("goto");
			WriteIdentifier(gotoStatement.Label);
			Semicolon();
			return EndNode(gotoStatement);
		}
		
		public object VisitIfElseStatement(IfElseStatement ifElseStatement, object data)
		{
			StartNode(ifElseStatement);
			WriteKeyword("if", IfElseStatement.IfKeywordRole);
			Space(policy.IfParentheses);
			LPar();
			Space(policy.WithinIfParentheses);
			ifElseStatement.Condition.AcceptVisitor(this, data);
			Space(policy.WithinIfParentheses);
			RPar();
			WriteEmbeddedStatement(ifElseStatement.TrueStatement);
			if (!ifElseStatement.FalseStatement.IsNull) {
				WriteKeyword("else", IfElseStatement.ElseKeywordRole);
				WriteEmbeddedStatement(ifElseStatement.FalseStatement);
			}
			return EndNode(ifElseStatement);
		}
		
		public object VisitLabelStatement(LabelStatement labelStatement, object data)
		{
			StartNode(labelStatement);
			WriteIdentifier(labelStatement.Label);
			WriteToken(":", LabelStatement.Roles.Colon);
			NewLine();
			return EndNode(labelStatement);
		}
		
		public object VisitLockStatement(LockStatement lockStatement, object data)
		{
			StartNode(lockStatement);
			WriteKeyword("lock");
			Space(policy.LockParentheses);
			LPar();
			Space(policy.WithinLockParentheses);
			lockStatement.Expression.AcceptVisitor(this, data);
			Space(policy.WithinLockParentheses);
			RPar();
			WriteEmbeddedStatement(lockStatement.EmbeddedStatement);
			return EndNode(lockStatement);
		}
		
		public object VisitReturnStatement(ReturnStatement returnStatement, object data)
		{
			StartNode(returnStatement);
			WriteKeyword("return");
			if (!returnStatement.Expression.IsNull) {
				Space();
				returnStatement.Expression.AcceptVisitor(this, data);
			}
			Semicolon();
			return EndNode(returnStatement);
		}
		
		public object VisitSwitchStatement(SwitchStatement switchStatement, object data)
		{
			StartNode(switchStatement);
			WriteKeyword("switch");
			Space(policy.SwitchParentheses);
			LPar();
			Space(policy.WithinSwitchParentheses);
			switchStatement.Expression.AcceptVisitor(this, data);
			Space(policy.WithinSwitchParentheses);
			RPar();
			OpenBrace(policy.StatementBraceStyle);
			foreach (var section in switchStatement.SwitchSections)
				section.AcceptVisitor(this, data);
			CloseBrace(policy.StatementBraceStyle);
			NewLine();
			return EndNode(switchStatement);
		}
		
		public object VisitSwitchSection(SwitchSection switchSection, object data)
		{
			StartNode(switchSection);
			foreach (var label in switchSection.CaseLabels)
				label.AcceptVisitor(this, data);
			foreach (var statement in switchSection.Statements)
				statement.AcceptVisitor(this, data);
			return EndNode(switchSection);
		}
		
		public object VisitCaseLabel(CaseLabel caseLabel, object data)
		{
			StartNode(caseLabel);
			WriteKeyword("case");
			Space();
			caseLabel.Expression.AcceptVisitor(this, data);
			WriteToken(":", CaseLabel.Roles.Colon);
			NewLine();
			return EndNode(caseLabel);
		}
		
		public object VisitThrowStatement(ThrowStatement throwStatement, object data)
		{
			StartNode(throwStatement);
			WriteKeyword("throw");
			if (!throwStatement.Expression.IsNull) {
				Space();
				throwStatement.Expression.AcceptVisitor(this, data);
			}
			Semicolon();
			return EndNode(throwStatement);
		}
		
		public object VisitTryCatchStatement(TryCatchStatement tryCatchStatement, object data)
		{
			StartNode(tryCatchStatement);
			WriteKeyword("try", TryCatchStatement.TryKeywordRole);
			tryCatchStatement.TryBlock.AcceptVisitor(this, data);
			foreach (var catchClause in tryCatchStatement.CatchClauses)
				catchClause.AcceptVisitor(this, data);
			if (!tryCatchStatement.FinallyBlock.IsNull) {
				WriteKeyword("finally", TryCatchStatement.FinallyKeywordRole);
				tryCatchStatement.FinallyBlock.AcceptVisitor(this, data);
			}
			return EndNode(tryCatchStatement);
		}
		
		public object VisitCatchClause(CatchClause catchClause, object data)
		{
			StartNode(catchClause);
			WriteKeyword("catch");
			if (!catchClause.Type.IsNull) {
				Space(policy.CatchParentheses);
				LPar();
				Space(policy.WithinCatchParentheses);
				catchClause.Type.AcceptVisitor(this, data);
				Space();
				WriteIdentifier(catchClause.VariableName);
				Space(policy.WithinCatchParentheses);
				RPar();
			}
			catchClause.Body.AcceptVisitor(this, data);
			return EndNode(catchClause);
		}
		
		public object VisitUncheckedStatement(UncheckedStatement uncheckedStatement, object data)
		{
			StartNode(uncheckedStatement);
			WriteKeyword("unchecked");
			uncheckedStatement.Body.AcceptVisitor(this, data);
			return EndNode(uncheckedStatement);
		}
		
		public object VisitUnsafeStatement(UnsafeStatement unsafeStatement, object data)
		{
			StartNode(unsafeStatement);
			WriteKeyword("unsafe");
			unsafeStatement.Body.AcceptVisitor(this, data);
			return EndNode(unsafeStatement);
		}
		
		public object VisitUsingStatement(UsingStatement usingStatement, object data)
		{
			StartNode(usingStatement);
			WriteKeyword("using");
			Space(policy.UsingParentheses);
			LPar();
			Space(policy.WithinUsingParentheses);
			
			usingStatement.ResourceAcquisition.AcceptVisitor(this, data);
			
			Space(policy.WithinUsingParentheses);
			RPar();
			
			WriteEmbeddedStatement(usingStatement.EmbeddedStatement);
			
			return EndNode(usingStatement);
		}
		
		public object VisitVariableDeclarationStatement(VariableDeclarationStatement variableDeclarationStatement, object data)
		{
			StartNode(variableDeclarationStatement);
			variableDeclarationStatement.Type.AcceptVisitor(this, data);
			Space();
			WriteCommaSeparatedList(variableDeclarationStatement.Variables);
			Semicolon();
			return EndNode(variableDeclarationStatement);
		}
		
		public object VisitWhileStatement(WhileStatement whileStatement, object data)
		{
			StartNode(whileStatement);
			WriteKeyword("while", WhileStatement.WhileKeywordRole);
			Space(policy.WhileParentheses);
			LPar();
			Space(policy.WithinWhileParentheses);
			whileStatement.Condition.AcceptVisitor(this, data);
			Space(policy.WithinWhileParentheses);
			RPar();
			WriteEmbeddedStatement(whileStatement.EmbeddedStatement);
			return EndNode(whileStatement);
		}
		
		public object VisitYieldBreakStatement(YieldBreakStatement yieldBreakStatement, object data)
		{
			StartNode(yieldBreakStatement);
			WriteKeyword("yield", YieldBreakStatement.YieldKeywordRole);
			WriteKeyword("break", YieldBreakStatement.BreakKeywordRole);
			Semicolon();
			return EndNode(yieldBreakStatement);
		}
		
		public object VisitYieldStatement(YieldStatement yieldStatement, object data)
		{
			StartNode(yieldStatement);
			WriteKeyword("yield", YieldStatement.YieldKeywordRole);
			WriteKeyword("return", YieldStatement.ReturnKeywordRole);
			Space();
			yieldStatement.Expression.AcceptVisitor(this, data);
			Semicolon();
			return EndNode(yieldStatement);
		}
		#endregion
		
		#region TypeMembers
		public object VisitAccessor(Accessor accessor, object data)
		{
			StartNode(accessor);
			WriteAttributes(accessor.Attributes);
			WriteModifiers(accessor.ModifierTokens);
			if (accessor.Role == PropertyDeclaration.GetterRole) {
				WriteKeyword("get");
			} else if (accessor.Role == PropertyDeclaration.SetterRole) {
				WriteKeyword("set");
			} else if (accessor.Role == CustomEventDeclaration.AddAccessorRole) {
				WriteKeyword("add");
			} else if (accessor.Role == CustomEventDeclaration.RemoveAccessorRole) {
				WriteKeyword("remove");
			}
			WriteMethodBody(accessor.Body);
			return EndNode(accessor);
		}
		
		public object VisitConstructorDeclaration(ConstructorDeclaration constructorDeclaration, object data)
		{
			StartNode(constructorDeclaration);
			WriteAttributes(constructorDeclaration.Attributes);
			WriteModifiers(constructorDeclaration.ModifierTokens);
			TypeDeclaration type = constructorDeclaration.Parent as TypeDeclaration;
			WriteIdentifier(type != null ? type.Name : constructorDeclaration.Name);
			Space(policy.BeforeConstructorDeclarationParentheses);
			WriteCommaSeparatedListInParenthesis(constructorDeclaration.Parameters, policy.WithinMethodDeclarationParentheses);
			if (!constructorDeclaration.Initializer.IsNull) {
				Space();
				constructorDeclaration.Initializer.AcceptVisitor(this, data);
			}
			WriteMethodBody(constructorDeclaration.Body);
			return EndNode(constructorDeclaration);
		}
		
		public object VisitConstructorInitializer(ConstructorInitializer constructorInitializer, object data)
		{
			StartNode(constructorInitializer);
			WriteToken(":", ConstructorInitializer.Roles.Colon);
			Space();
			if (constructorInitializer.ConstructorInitializerType == ConstructorInitializerType.This) {
				WriteKeyword("this");
			} else {
				WriteKeyword("base");
			}
			Space(policy.BeforeMethodCallParentheses);
			WriteCommaSeparatedListInParenthesis(constructorInitializer.Arguments, policy.WithinMethodCallParentheses);
			return EndNode(constructorInitializer);
		}
		
		public object VisitDestructorDeclaration(DestructorDeclaration destructorDeclaration, object data)
		{
			StartNode(destructorDeclaration);
			WriteAttributes(destructorDeclaration.Attributes);
			WriteModifiers(destructorDeclaration.ModifierTokens);
			WriteToken("~", DestructorDeclaration.TildeRole);
			TypeDeclaration type = destructorDeclaration.Parent as TypeDeclaration;
			WriteIdentifier(type != null ? type.Name : destructorDeclaration.Name);
			Space(policy.BeforeConstructorDeclarationParentheses);
			LPar();
			RPar();
			WriteMethodBody(destructorDeclaration.Body);
			return EndNode(destructorDeclaration);
		}
		
		public object VisitEnumMemberDeclaration(EnumMemberDeclaration enumMemberDeclaration, object data)
		{
			StartNode(enumMemberDeclaration);
			WriteAttributes(enumMemberDeclaration.Attributes);
			WriteModifiers(enumMemberDeclaration.ModifierTokens);
			WriteIdentifier(enumMemberDeclaration.Name);
			if (!enumMemberDeclaration.Initializer.IsNull) {
				Space(policy.AroundAssignmentParentheses);
				WriteToken("=", EnumMemberDeclaration.Roles.Assign);
				Space(policy.AroundAssignmentParentheses);
				enumMemberDeclaration.Initializer.AcceptVisitor(this, data);
			}
			return EndNode(enumMemberDeclaration);
		}
		
		public object VisitEventDeclaration(EventDeclaration eventDeclaration, object data)
		{
			StartNode(eventDeclaration);
			WriteAttributes(eventDeclaration.Attributes);
			WriteModifiers(eventDeclaration.ModifierTokens);
			WriteKeyword("event");
			eventDeclaration.ReturnType.AcceptVisitor(this, data);
			Space();
			WriteCommaSeparatedList(eventDeclaration.Variables);
			Semicolon();
			return EndNode(eventDeclaration);
		}
		
		public object VisitCustomEventDeclaration(CustomEventDeclaration customEventDeclaration, object data)
		{
			StartNode(customEventDeclaration);
			WriteAttributes(customEventDeclaration.Attributes);
			WriteModifiers(customEventDeclaration.ModifierTokens);
			WriteKeyword("event");
			customEventDeclaration.ReturnType.AcceptVisitor(this, data);
			Space();
			WritePrivateImplementationType(customEventDeclaration.PrivateImplementationType);
			WriteIdentifier(customEventDeclaration.Name);
			OpenBrace(policy.EventBraceStyle);
			// output add/remove in their original order
			foreach (AstNode node in customEventDeclaration.Children) {
				if (node.Role == CustomEventDeclaration.AddAccessorRole || node.Role == CustomEventDeclaration.RemoveAccessorRole) {
					node.AcceptVisitor(this, data);
				}
			}
			CloseBrace(policy.EventBraceStyle);
			NewLine();
			return EndNode(customEventDeclaration);
		}
		
		public object VisitFieldDeclaration(FieldDeclaration fieldDeclaration, object data)
		{
			StartNode(fieldDeclaration);
			WriteAttributes(fieldDeclaration.Attributes);
			WriteModifiers(fieldDeclaration.ModifierTokens);
			fieldDeclaration.ReturnType.AcceptVisitor(this, data);
			Space();
			WriteCommaSeparatedList(fieldDeclaration.Variables);
			Semicolon();
			return EndNode(fieldDeclaration);
		}
		
		public object VisitIndexerDeclaration(IndexerDeclaration indexerDeclaration, object data)
		{
			StartNode(indexerDeclaration);
			WriteAttributes(indexerDeclaration.Attributes);
			WriteModifiers(indexerDeclaration.ModifierTokens);
			indexerDeclaration.ReturnType.AcceptVisitor(this, data);
			WritePrivateImplementationType(indexerDeclaration.PrivateImplementationType);
			WriteKeyword("this");
			Space(policy.BeforeMethodDeclarationParentheses);
			WriteCommaSeparatedListInParenthesis(indexerDeclaration.Parameters, policy.WithinMethodDeclarationParentheses);
			OpenBrace(policy.PropertyBraceStyle);
			// output get/set in their original order
			foreach (AstNode node in indexerDeclaration.Children) {
				if (node.Role == IndexerDeclaration.GetterRole || node.Role == IndexerDeclaration.SetterRole) {
					node.AcceptVisitor(this, data);
				}
			}
			CloseBrace(policy.PropertyBraceStyle);
			NewLine();
			return EndNode(indexerDeclaration);
		}
		
		public object VisitMethodDeclaration(MethodDeclaration methodDeclaration, object data)
		{
			StartNode(methodDeclaration);
			WriteAttributes(methodDeclaration.Attributes);
			WriteModifiers(methodDeclaration.ModifierTokens);
			methodDeclaration.ReturnType.AcceptVisitor(this, data);
			Space();
			WritePrivateImplementationType(methodDeclaration.PrivateImplementationType);
			WriteIdentifier(methodDeclaration.Name);
			WriteTypeParameters(methodDeclaration.TypeParameters);
			Space(policy.BeforeMethodDeclarationParentheses);
			WriteCommaSeparatedListInParenthesis(methodDeclaration.Parameters, policy.WithinMethodDeclarationParentheses);
			foreach (Constraint constraint in methodDeclaration.Constraints) {
				constraint.AcceptVisitor(this, data);
			}
			WriteMethodBody(methodDeclaration.Body);
			return EndNode(methodDeclaration);
		}
		
		public object VisitOperatorDeclaration(OperatorDeclaration operatorDeclaration, object data)
		{
			StartNode(operatorDeclaration);
			WriteAttributes(operatorDeclaration.Attributes);
			WriteModifiers(operatorDeclaration.ModifierTokens);
			if (operatorDeclaration.OperatorType == OperatorType.Explicit) {
				WriteKeyword("explicit", OperatorDeclaration.OperatorTypeRole);
			} else if (operatorDeclaration.OperatorType == OperatorType.Implicit) {
				WriteKeyword("implicit", OperatorDeclaration.OperatorTypeRole);
			} else {
				operatorDeclaration.ReturnType.AcceptVisitor(this, data);
			}
			WriteKeyword("operator", OperatorDeclaration.OperatorKeywordRole);
			Space();
			if (operatorDeclaration.OperatorType == OperatorType.Explicit
			    || operatorDeclaration.OperatorType == OperatorType.Implicit)
			{
				operatorDeclaration.ReturnType.AcceptVisitor(this, data);
			} else {
				WriteToken(OperatorDeclaration.GetToken(operatorDeclaration.OperatorType), OperatorDeclaration.OperatorTypeRole);
			}
			Space(policy.BeforeMethodDeclarationParentheses);
			WriteCommaSeparatedListInParenthesis(operatorDeclaration.Parameters, policy.WithinMethodDeclarationParentheses);
			WriteMethodBody(operatorDeclaration.Body);
			return EndNode(operatorDeclaration);
		}
		
		public object VisitParameterDeclaration(ParameterDeclaration parameterDeclaration, object data)
		{
			StartNode(parameterDeclaration);
			WriteAttributes(parameterDeclaration.Attributes);
			switch (parameterDeclaration.ParameterModifier) {
				case ParameterModifier.Ref:
					WriteKeyword("ref", ParameterDeclaration.ModifierRole);
					break;
				case ParameterModifier.Out:
					WriteKeyword("out", ParameterDeclaration.ModifierRole);
					break;
				case ParameterModifier.Params:
					WriteKeyword("params", ParameterDeclaration.ModifierRole);
					break;
				case ParameterModifier.This:
					WriteKeyword("this", ParameterDeclaration.ModifierRole);
					break;
			}
			parameterDeclaration.Type.AcceptVisitor(this, data);
			if (!parameterDeclaration.Type.IsNull && !string.IsNullOrEmpty(parameterDeclaration.Name))
				Space();
			if (!string.IsNullOrEmpty(parameterDeclaration.Name))
				WriteIdentifier(parameterDeclaration.Name);
			if (!parameterDeclaration.DefaultExpression.IsNull) {
				Space(policy.AroundAssignmentParentheses);
				WriteToken("=", ParameterDeclaration.Roles.Assign);
				Space(policy.AroundAssignmentParentheses);
				parameterDeclaration.DefaultExpression.AcceptVisitor(this, data);
			}
			return EndNode(parameterDeclaration);
		}
		
		public object VisitPropertyDeclaration(PropertyDeclaration propertyDeclaration, object data)
		{
			StartNode(propertyDeclaration);
			WriteAttributes(propertyDeclaration.Attributes);
			WriteModifiers(propertyDeclaration.ModifierTokens);
			propertyDeclaration.ReturnType.AcceptVisitor(this, data);
			Space();
			WritePrivateImplementationType(propertyDeclaration.PrivateImplementationType);
			WriteIdentifier(propertyDeclaration.Name);
			OpenBrace(policy.PropertyBraceStyle);
			// output get/set in their original order
			foreach (AstNode node in propertyDeclaration.Children) {
				if (node.Role == IndexerDeclaration.GetterRole || node.Role == IndexerDeclaration.SetterRole) {
					node.AcceptVisitor(this, data);
				}
			}
			CloseBrace(policy.PropertyBraceStyle);
			NewLine();
			return EndNode(propertyDeclaration);
		}
		#endregion
		
		#region Other nodes
		public object VisitVariableInitializer(VariableInitializer variableInitializer, object data)
		{
			StartNode(variableInitializer);
			WriteIdentifier(variableInitializer.Name);
			if (!variableInitializer.Initializer.IsNull) {
				Space(policy.AroundAssignmentParentheses);
				WriteToken("=", VariableInitializer.Roles.Assign);
				Space(policy.AroundAssignmentParentheses);
				variableInitializer.Initializer.AcceptVisitor(this, data);
			}
			return EndNode(variableInitializer);
		}
		
		public object VisitCompilationUnit(CompilationUnit compilationUnit, object data)
		{
			// don't do node tracking as we visit all children directly
			foreach (AstNode node in compilationUnit.Children)
				node.AcceptVisitor(this, data);
			return null;
		}
		
		public object VisitSimpleType(SimpleType simpleType, object data)
		{
			StartNode(simpleType);
			WriteIdentifier(simpleType.Identifier);
			WriteTypeArguments(simpleType.TypeArguments);
			return EndNode(simpleType);
		}
		
		public object VisitMemberType(MemberType memberType, object data)
		{
			StartNode(memberType);
			memberType.Target.AcceptVisitor(this, data);
			if (memberType.IsDoubleColon)
				WriteToken("::", MemberType.Roles.Dot);
			else
				WriteToken(".", MemberType.Roles.Dot);
			WriteIdentifier(memberType.MemberName);
			WriteTypeArguments(memberType.TypeArguments);
			return EndNode(memberType);
		}
		
		public object VisitComposedType(ComposedType composedType, object data)
		{
			StartNode(composedType);
			composedType.BaseType.AcceptVisitor(this, data);
			if (composedType.HasNullableSpecifier)
				WriteToken("?", ComposedType.NullableRole);
			for (int i = 0; i < composedType.PointerRank; i++)
				WriteToken("*", ComposedType.PointerRole);
			foreach (var node in composedType.ArraySpecifiers)
				node.AcceptVisitor(this, data);
			return EndNode(composedType);
		}
		
		public object VisitArraySpecifier(ArraySpecifier arraySpecifier, object data)
		{
			StartNode(arraySpecifier);
			WriteToken("[", ArraySpecifier.Roles.LBracket);
			foreach (var comma in arraySpecifier.GetChildrenByRole(ArraySpecifier.Roles.Comma)) {
				WriteSpecialsUpToNode(comma);
				formatter.WriteToken(",");
				lastWritten = LastWritten.Other;
			}
			WriteToken("]", ArraySpecifier.Roles.RBracket);
			return EndNode(arraySpecifier);
		}
		
		public object VisitPrimitiveType(PrimitiveType primitiveType, object data)
		{
			StartNode(primitiveType);
			WriteKeyword(primitiveType.Keyword);
			if (primitiveType.Keyword == "new") {
				// new() constraint
				LPar();
				RPar();
			}
			return EndNode(primitiveType);
		}
		
		public object VisitComment(Comment comment, object data)
		{
			if (lastWritten == LastWritten.Division) {
				// When there's a comment starting after a division operator
				// "1.0 / /*comment*/a", then we need to insert a space in front of the comment.
				formatter.Space();
			}
			formatter.WriteComment(comment.CommentType, comment.Content);
			lastWritten = LastWritten.Whitespace;
			return null;
		}
		
		public object VisitTypeParameterDeclaration(TypeParameterDeclaration typeParameterDeclaration, object data)
		{
			StartNode(typeParameterDeclaration);
			WriteAttributes(typeParameterDeclaration.Attributes);
			switch (typeParameterDeclaration.Variance) {
				case VarianceModifier.Invariant:
					break;
				case VarianceModifier.Covariant:
					WriteKeyword("out");
					break;
				case VarianceModifier.Contravariant:
					WriteKeyword("in");
					break;
				default:
					throw new NotSupportedException("Invalid value for VarianceModifier");
			}
			WriteIdentifier(typeParameterDeclaration.Name);
			return EndNode(typeParameterDeclaration);
		}
		
		public object VisitConstraint(Constraint constraint, object data)
		{
			StartNode(constraint);
			Space();
			WriteKeyword("where");
			WriteIdentifier(constraint.TypeParameter);
			Space();
			WriteToken(":", Constraint.ColonRole);
			Space();
			WriteCommaSeparatedList(constraint.BaseTypes);
			return EndNode(constraint);
		}
		
		public object VisitCSharpTokenNode(CSharpTokenNode cSharpTokenNode, object data)
		{
			CSharpModifierToken mod = cSharpTokenNode as CSharpModifierToken;
			if (mod != null) {
				StartNode(mod);
				WriteKeyword(CSharpModifierToken.GetModifierName(mod.Modifier));
				return EndNode(mod);
			} else {
				throw new NotSupportedException("Should never visit individual tokens");
			}
		}
		
		public object VisitIdentifier(Identifier identifier, object data)
		{
			StartNode(identifier);
			WriteIdentifier(identifier.Name);
			return EndNode(identifier);
		}
		#endregion
		
		#region Pattern Nodes
		object IPatternAstVisitor<object, object>.VisitPlaceholder(AstNode placeholder, AstNode child, object data)
		{
			StartNode(placeholder);
			child.AcceptVisitor(this, data);
			return EndNode(placeholder);
		}
		
		object IPatternAstVisitor<object, object>.VisitAnyNode(AnyNode anyNode, object data)
		{
			StartNode(anyNode);
			if (!string.IsNullOrEmpty(anyNode.GroupName)) {
				WriteIdentifier(anyNode.GroupName);
				WriteToken(":", AstNode.Roles.Colon);
			}
			WriteKeyword("anyNode");
			return EndNode(anyNode);
		}
		
		object IPatternAstVisitor<object, object>.VisitBackreference(Backreference backreference, object data)
		{
			StartNode(backreference);
			WriteKeyword("backreference");
			LPar();
			WriteIdentifier(backreference.ReferencedGroupName);
			RPar();
			return EndNode(backreference);
		}
		
		object IPatternAstVisitor<object, object>.VisitIdentifierExpressionBackreference(IdentifierExpressionBackreference identifierExpressionBackreference, object data)
		{
			StartNode(identifierExpressionBackreference);
			WriteKeyword("identifierBackreference");
			LPar();
			WriteIdentifier(identifierExpressionBackreference.ReferencedGroupName);
			RPar();
			return EndNode(identifierExpressionBackreference);
		}
		
		object IPatternAstVisitor<object, object>.VisitChoice(Choice choice, object data)
		{
			StartNode(choice);
			WriteKeyword("choice");
			Space();
			LPar();
			NewLine();
			formatter.Indent();
			foreach (AstNode alternative in choice) {
				alternative.AcceptVisitor(this, data);
				if (alternative != choice.LastChild)
					WriteToken(",", AstNode.Roles.Comma);
				NewLine();
			}
			formatter.Unindent();
			RPar();
			return EndNode(choice);
		}
		
		object IPatternAstVisitor<object, object>.VisitNamedNode(NamedNode namedNode, object data)
		{
			StartNode(namedNode);
			if (!string.IsNullOrEmpty(namedNode.GroupName)) {
				WriteIdentifier(namedNode.GroupName);
				WriteToken(":", AstNode.Roles.Colon);
			}
			namedNode.GetChildByRole(NamedNode.ElementRole).AcceptVisitor(this, data);
			return EndNode(namedNode);
		}
		
		object IPatternAstVisitor<object, object>.VisitRepeat(Repeat repeat, object data)
		{
			StartNode(repeat);
			WriteKeyword("repeat");
			LPar();
			if (repeat.MinCount != 0 || repeat.MaxCount != int.MaxValue) {
				WriteIdentifier(repeat.MinCount.ToString());
				WriteToken(",", AstNode.Roles.Comma);
				WriteIdentifier(repeat.MaxCount.ToString());
				WriteToken(",", AstNode.Roles.Comma);
			}
			repeat.GetChildByRole(Repeat.ElementRole).AcceptVisitor(this, data);
			RPar();
			return EndNode(repeat);
		}
		#endregion
	}
}
