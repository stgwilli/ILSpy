﻿// Copyright (c) 2011 AlphaSierraPapa for the SharpDevelop Team
// 
// Permission is hereby granted, free of charge, to any person obtaining a copy of this
// software and associated documentation files (the "Software"), to deal in the Software
// without restriction, including without limitation the rights to use, copy, modify, merge,
// publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons
// to whom the Software is furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in all copies or
// substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED,
// INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR
// PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE
// FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR
// OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER
// DEALINGS IN THE SOFTWARE.

using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Threading;

using ICSharpCode.Decompiler;
using ICSharpCode.TreeView;
using Mono.Cecil;

namespace ICSharpCode.ILSpy.TreeNodes
{
	/// <summary>
	/// Lists the base types of a class.
	/// </summary>
	sealed class BaseTypesTreeNode : ILSpyTreeNode
	{
		readonly TypeDefinition type;
		
		public BaseTypesTreeNode(TypeDefinition type)
		{
			this.type = type;
			this.LazyLoading = true;
		}
		
		public override object Text {
			get { return "Base Types"; }
		}
		
		public override object Icon {
			get { return Images.SuperTypes; }
		}
		
		protected override void LoadChildren()
		{
			AddBaseTypes(this.Children, type);
		}
		
		internal static void AddBaseTypes(SharpTreeNodeCollection children, TypeDefinition type)
		{
			if (type.BaseType != null)
				children.Add(new BaseTypesEntryNode(type.BaseType, false));
			foreach (TypeReference i in type.Interfaces) {
				children.Add(new BaseTypesEntryNode(i, true));
			}
		}
		
		public override void Decompile(Language language, ITextOutput output, DecompilationOptions options)
		{
			App.Current.Dispatcher.Invoke(DispatcherPriority.Normal, new Action(EnsureLazyChildren));
			foreach (ILSpyTreeNode child in this.Children) {
				child.Decompile(language, output, options);
			}
		}
	}
	
	sealed class BaseTypesEntryNode : ILSpyTreeNode
	{
		TypeReference tr;
		TypeDefinition def;
		bool isInterface;
		
		public BaseTypesEntryNode(TypeReference tr, bool isInterface)
		{
			if (tr == null)
				throw new ArgumentNullException("tr");
			this.tr = tr;
			this.def = tr.Resolve();
			this.isInterface = isInterface;
			this.LazyLoading = true;
		}
		
		public override bool ShowExpander {
			get {
				return def != null && (def.BaseType != null || def.HasInterfaces);
			}
		}
		
		public override object Text {
			get { return tr.FullName; }
		}
		
		public override object Icon {
			get {
				if (def != null)
					return TypeTreeNode.GetIcon(def);
				else
					return isInterface ? Images.Interface : Images.Class;
			}
		}
		
		protected override void LoadChildren()
		{
			if (def != null)
				BaseTypesTreeNode.AddBaseTypes(this.Children, def);
		}
		
		public override void ActivateItem(System.Windows.RoutedEventArgs e)
		{
			// on item activation, try to resolve once again (maybe the user loaded the assembly in the meantime)
			if (def == null) {
				def = tr.Resolve();
				if (def != null)
					this.LazyLoading = true; // re-load children
			}
			e.Handled = ActivateItem(this, def);
		}
		
		internal static bool ActivateItem(SharpTreeNode node, TypeDefinition def)
		{
			if (def != null) {
				var assemblyListNode = node.Ancestors().OfType<AssemblyListTreeNode>().FirstOrDefault();
				if (assemblyListNode != null) {
					assemblyListNode.Select(assemblyListNode.FindTypeNode(def));
					return true;
				}
			}
			return false;
		}
		
		public override void Decompile(Language language, ITextOutput output, DecompilationOptions options)
		{
			language.WriteCommentLine(output, language.TypeToString(tr, true));
		}
	}
}
