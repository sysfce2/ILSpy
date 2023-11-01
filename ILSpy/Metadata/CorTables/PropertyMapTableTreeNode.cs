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
using System.Collections.Generic;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;

using ICSharpCode.Decompiler;
using ICSharpCode.Decompiler.Disassembler;
using ICSharpCode.Decompiler.IL;
using ICSharpCode.Decompiler.Metadata;

namespace ICSharpCode.ILSpy.Metadata
{
	class PropertyMapTableTreeNode : MetadataTableTreeNode
	{
		public PropertyMapTableTreeNode(PEFile module)
			: base((HandleKind)0x15, module)
		{
		}

		public override object Text => $"15 PropertyMap ({module.Metadata.GetTableRowCount(TableIndex.PropertyMap)})";

		public override object Icon => Images.Literal;

		public override bool View(ViewModels.TabPageModel tabPage)
		{
			tabPage.Title = Text.ToString();
			tabPage.SupportsLanguageSwitching = false;

			var view = Helpers.PrepareDataGrid(tabPage, this);
			var metadata = module.Metadata;

			var list = new List<PropertyMapEntry>();
			PropertyMapEntry scrollTargetEntry = default;

			var length = metadata.GetTableRowCount(TableIndex.PropertyMap);
			ReadOnlySpan<byte> ptr = metadata.AsReadOnlySpan();
			int metadataOffset = module.Reader.PEHeaders.MetadataStartOffset;
			for (int rid = 1; rid <= length; rid++)
			{
				PropertyMapEntry entry = new PropertyMapEntry(module, ptr, metadataOffset, rid);
				if (entry.RID == this.scrollTarget)
				{
					scrollTargetEntry = entry;
				}
				list.Add(entry);
			}

			view.ItemsSource = list;

			tabPage.Content = view;

			if (scrollTargetEntry.RID > 0)
			{
				ScrollItemIntoView(view, scrollTargetEntry);
			}

			return true;
		}

		readonly struct PropertyMap
		{
			public readonly TypeDefinitionHandle Parent;
			public readonly PropertyDefinitionHandle PropertyList;

			public PropertyMap(ReadOnlySpan<byte> ptr, int typeDefSize, int propertyDefSize)
			{
				Parent = MetadataTokens.TypeDefinitionHandle(Helpers.GetValueLittleEndian(ptr, typeDefSize));
				PropertyList = MetadataTokens.PropertyDefinitionHandle(Helpers.GetValueLittleEndian(ptr.Slice(typeDefSize, propertyDefSize)));
			}
		}

		struct PropertyMapEntry
		{
			readonly PEFile module;
			readonly MetadataReader metadata;
			readonly PropertyMap propertyMap;

			public int RID { get; }

			public int Token => 0x15000000 | RID;

			public int Offset { get; }

			[ColumnInfo("X8", Kind = ColumnKind.Token)]
			public int Parent => MetadataTokens.GetToken(propertyMap.Parent);

			public void OnParentClick()
			{
				MainWindow.Instance.JumpToReference(new EntityReference(module, propertyMap.Parent, protocol: "metadata"));
			}

			string parentTooltip;
			public string ParentTooltip => GenerateTooltip(ref parentTooltip, module, propertyMap.Parent);

			[ColumnInfo("X8", Kind = ColumnKind.Token)]
			public int PropertyList => MetadataTokens.GetToken(propertyMap.PropertyList);

			public void OnPropertyListClick()
			{
				MainWindow.Instance.JumpToReference(new EntityReference(module, propertyMap.PropertyList, protocol: "metadata"));
			}

			string propertyListTooltip;
			public string PropertyListTooltip => GenerateTooltip(ref propertyListTooltip, module, propertyMap.PropertyList);

			public PropertyMapEntry(PEFile module, ReadOnlySpan<byte> ptr, int metadataOffset, int row)
			{
				this.module = module;
				this.metadata = module.Metadata;
				this.RID = row;
				var rowOffset = metadata.GetTableMetadataOffset(TableIndex.PropertyMap)
					+ metadata.GetTableRowSize(TableIndex.PropertyMap) * (row - 1);
				this.Offset = metadataOffset + rowOffset;
				int typeDefSize = metadata.GetTableRowCount(TableIndex.TypeDef) < ushort.MaxValue ? 2 : 4;
				int propertyDefSize = metadata.GetTableRowCount(TableIndex.Property) < ushort.MaxValue ? 2 : 4;
				this.propertyMap = new PropertyMap(ptr.Slice(rowOffset), typeDefSize, propertyDefSize);
				this.propertyListTooltip = null;
				this.parentTooltip = null;
			}
		}

		public override void Decompile(Language language, ITextOutput output, DecompilationOptions options)
		{
			language.WriteCommentLine(output, "PropertyMap");
		}
	}
}
