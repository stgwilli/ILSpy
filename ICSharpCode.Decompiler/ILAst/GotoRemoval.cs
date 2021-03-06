﻿using System;
using System.Diagnostics;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using Decompiler.ControlFlow;

namespace Decompiler
{
	public class GotoRemoval
	{
		Dictionary<ILNode, ILNode> parent = new Dictionary<ILNode, ILNode>();
		Dictionary<ILNode, ILNode> nextSibling = new Dictionary<ILNode, ILNode>();
		
		public void RemoveGotos(ILBlock method)
		{
			// Build the navigation data
			parent[method] = null;
			foreach (ILNode node in method.GetSelfAndChildrenRecursive<ILNode>()) {
				ILNode previousChild = null;
				foreach (ILNode child in node.GetChildren()) {
					Debug.Assert(!parent.ContainsKey(child));
					parent[child] = node;
					if (previousChild != null)
						nextSibling[previousChild] = child;
					previousChild = child;
				}
				if (previousChild != null)
					nextSibling[previousChild] = null;
			}
			
			// Simplify gotos
			foreach (ILExpression gotoExpr in method.GetSelfAndChildrenRecursive<ILExpression>().Where(e => e.Code == ILCode.Br || e.Code == ILCode.Leave)) {
				TrySimplifyGoto(gotoExpr);
			}
			
			RemoveRedundantCode(method);
		}
		
		public static void RemoveRedundantCode(ILBlock method)
		{
			// Remove dead lables and nops
			HashSet<ILLabel> liveLabels = new HashSet<ILLabel>(method.GetSelfAndChildrenRecursive<ILExpression>().SelectMany(e => e.GetBranchTargets()));
			foreach(ILBlock block in method.GetSelfAndChildrenRecursive<ILBlock>().ToList()) {
				block.Body = block.Body.Where(n => !n.Match(ILCode.Nop) && !(n is ILLabel && !liveLabels.Contains((ILLabel)n))).ToList();
			}
			
			// Remove redundant continue
			foreach(ILWhileLoop loop in method.GetSelfAndChildrenRecursive<ILWhileLoop>()) {
				var body = loop.BodyBlock.Body;
				if (body.Count > 0 && body.Last().Match(ILCode.LoopContinue)) {
					body.RemoveAt(body.Count - 1);
				}
			}
			
			// Remove redundant return
			if (method.Body.Count > 0 && method.Body.Last().Match(ILCode.Ret) && ((ILExpression)method.Body.Last()).Arguments.Count == 0) {
				method.Body.RemoveAt(method.Body.Count - 1);
			}
		}
		
		bool TrySimplifyGoto(ILExpression gotoExpr)
		{
			Debug.Assert(gotoExpr.Code == ILCode.Br || gotoExpr.Code == ILCode.Leave);
			Debug.Assert(gotoExpr.Prefixes == null);
			Debug.Assert(gotoExpr.Operand != null);
			
			ILExpression target = Enter(gotoExpr, new HashSet<ILNode>());
			if (target == null)
				return false;
			
			if (target == Exit(gotoExpr, new HashSet<ILNode>())) {
				gotoExpr.Code = ILCode.Nop;
				gotoExpr.Operand = null;
				target.ILRanges.AddRange(gotoExpr.ILRanges);
				gotoExpr.ILRanges.Clear();
				return true;
			}
			
			ILWhileLoop loop = null;
			ILNode current = gotoExpr;
			while(loop == null && current != null) {
				current = parent[current];
				loop = current as ILWhileLoop;
			}
			
			if (loop != null && target == Exit(loop, new HashSet<ILNode>())) {
				gotoExpr.Code = ILCode.LoopBreak;
				gotoExpr.Operand = null;
				return true;
			}
			
			if (loop != null && target == Enter(loop, new HashSet<ILNode>())) {
				gotoExpr.Code = ILCode.LoopContinue;
				gotoExpr.Operand = null;
				return true;
			}
			
			return false;
		}
		
		/// <summary>
		/// Get the first expression to be excecuted if the instruction pointer is at the start of the given node
		/// </summary>
		ILExpression Enter(ILNode node, HashSet<ILNode> visitedNodes)
		{
			if (node == null)
				throw new ArgumentNullException();
			
			if (!visitedNodes.Add(node))
				return null;  // Infinite loop
			
			ILLabel label = node as ILLabel;
			if (label != null) {
				return Exit(label, visitedNodes);
			}
			
			ILExpression expr = node as ILExpression;
			if (expr != null) {
				if (expr.Code == ILCode.Br || expr.Code == ILCode.Leave) {
					return Enter((ILLabel)expr.Operand, visitedNodes);
				} else if (expr.Code == ILCode.Nop) {
					return Exit(expr, visitedNodes);
				} else {
					return expr;
				}
			}
			
			ILBlock block = node as ILBlock;
			if (block != null) {
				if (block.EntryGoto != null) {
					return Enter(block.EntryGoto, visitedNodes);
				} else if (block.Body.Count > 0) {
					return Enter(block.Body[0], visitedNodes);
				} else {
					return Exit(block, visitedNodes);
				}
			}
			
			ILCondition cond = node as ILCondition;
			if (cond != null) {
				return cond.Condition;
			}
			
			ILWhileLoop loop = node as ILWhileLoop;
			if (loop != null) {
				if (loop.Condition != null) {
					return loop.Condition;
				} else {
					return Enter(loop.BodyBlock, visitedNodes);
				}
			}
			
			ILTryCatchBlock tryCatch = node as ILTryCatchBlock;
			if (tryCatch != null) {
				return Enter(tryCatch.TryBlock, visitedNodes);
			}
			
			ILSwitch ilSwitch = node as ILSwitch;
			if (ilSwitch != null) {
				return ilSwitch.Condition;
			}
			
			throw new NotSupportedException(node.GetType().ToString());
		}
		
		/// <summary>
		/// Get the first expression to be excecuted if the instruction pointer is at the end of the given node
		/// </summary>
		ILExpression Exit(ILNode node, HashSet<ILNode> visitedNodes)
		{
			if (node == null)
				throw new ArgumentNullException();
			
			ILNode nodeParent = parent[node];
			if (nodeParent == null)
				return null;  // Exited main body
			
			if (nodeParent is ILBlock) {
				ILNode nextNode = nextSibling[node];
				if (nextNode != null) {
					return Enter(nextNode, visitedNodes);
				} else {
					return Exit(nodeParent, visitedNodes);
				}
			}
			
			if (nodeParent is ILCondition ||
			    nodeParent is ILTryCatchBlock ||
			    nodeParent is ILSwitch)
			{
				return Exit(nodeParent, visitedNodes);
			}
			
			if (nodeParent is ILWhileLoop) {
				return Enter(nodeParent, visitedNodes);
			}
			
			throw new NotSupportedException(nodeParent.GetType().ToString());
		}
	}
}
