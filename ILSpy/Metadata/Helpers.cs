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
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Navigation;

using DataGridExtensions;

using ICSharpCode.Decompiler.Util;
using ICSharpCode.ILSpy.Controls;
using ICSharpCode.ILSpy.TextView;
using ICSharpCode.ILSpy.TreeNodes;
using ICSharpCode.ILSpy.ViewModels;

namespace ICSharpCode.ILSpy.Metadata
{
	static class Helpers
	{
		public static DataGrid PrepareDataGrid(TabPageModel tabPage, ILSpyTreeNode selectedNode)
		{
			if (!(tabPage.Content is DataGrid view && view.Name == "MetadataView"))
			{
				view = new MetaDataGrid() {
					Name = "MetadataView",
					GridLinesVisibility = DataGridGridLinesVisibility.None,
					CanUserAddRows = false,
					CanUserDeleteRows = false,
					CanUserReorderColumns = false,
					HeadersVisibility = DataGridHeadersVisibility.Column,
					EnableColumnVirtualization = true,
					EnableRowVirtualization = true,
					RowHeight = 20,
					IsReadOnly = true,
					SelectionMode = DataGridSelectionMode.Single,
					SelectionUnit = DataGridSelectionUnit.FullRow,
					SelectedTreeNode = selectedNode,
					CellStyle = (Style)MetadataTableViews.Instance["DataGridCellStyle"],
				};
				ContextMenuProvider.Add(view);
				DataGridFilter.SetIsAutoFilterEnabled(view, true);
				DataGridFilter.SetContentFilterFactory(view, new RegexContentFilterFactory());
			}
			DataGridFilter.GetFilter(view).Clear();
			view.RowDetailsTemplateSelector = null;
			view.RowDetailsVisibilityMode = DataGridRowDetailsVisibilityMode.Collapsed;
			view.EnableColumnVirtualization = true;
			view.EnableRowVirtualization = true;
			((MetaDataGrid)view).SelectedTreeNode = selectedNode;
			if (!view.AutoGenerateColumns)
				view.Columns.Clear();
			view.AutoGenerateColumns = true;

			view.AutoGeneratingColumn += View_AutoGeneratingColumn;
			view.AutoGeneratedColumns += View_AutoGeneratedColumns;

			return view;
		}

		internal static void View_AutoGeneratedColumns(object sender, EventArgs e)
		{
			((DataGrid)sender).AutoGeneratedColumns -= View_AutoGeneratedColumns;
			((DataGrid)sender).AutoGeneratingColumn -= View_AutoGeneratingColumn;
		}

		internal static void View_AutoGeneratingColumn(object sender, DataGridAutoGeneratingColumnEventArgs e)
		{
			var binding = new Binding(e.PropertyName) { Mode = BindingMode.OneWay };
			e.Column = GetColumn();
			switch (e.PropertyName)
			{
				case "RID":
				case "Meaning":
					e.Column.SetTemplate((ControlTemplate)MetadataTableViews.Instance["DefaultFilter"]);
					break;
				case "Token":
				case "Offset":
				case "RVA":
				case "StartOffset":
				case "Length":
					binding.StringFormat = "X8";
					e.Column.SetTemplate((ControlTemplate)MetadataTableViews.Instance["HexFilter"]);
					break;
				case "RowDetails":
					e.Cancel = true;
					break;
				case "Value" when e.PropertyDescriptor is PropertyDescriptor dp && dp.ComponentType == typeof(Entry):
					binding.Path = new PropertyPath(".");
					binding.Converter = ByteWidthConverter.Instance;
					break;
				default:
					e.Cancel = e.PropertyName.Contains("Tooltip");
					if (!e.Cancel)
					{
						e.Column.SetTemplate((ControlTemplate)MetadataTableViews.Instance["DefaultFilter"]);
					}
					break;
			}
			if (!e.Cancel)
			{
				ApplyAttributes((PropertyDescriptor)e.PropertyDescriptor, binding, e.Column);
			}

			DataGridColumn GetColumn()
			{
				if (e.PropertyType == typeof(bool))
				{
					return new DataGridCheckBoxColumn() {
						Header = e.PropertyName,
						SortMemberPath = e.PropertyName,
						Binding = binding
					};
				}

				var descriptor = (PropertyDescriptor)e.PropertyDescriptor;

				if (descriptor.Attributes.OfType<ColumnInfoAttribute>().Any(c => c.Kind == ColumnKind.Token || c.LinkToTable))
				{
					return new DataGridTemplateColumn() {
						Header = e.PropertyName,
						SortMemberPath = e.PropertyName,
						CellTemplate = GetOrCreateLinkCellTemplate(e.PropertyName, descriptor, binding)
					};
				}

				return new DataGridTextColumn() {
					Header = e.PropertyName,
					SortMemberPath = e.PropertyName,
					Binding = binding
				};
			}
		}

		static readonly Dictionary<string, DataTemplate> linkCellTemplates = new Dictionary<string, DataTemplate>();

		private static DataTemplate GetOrCreateLinkCellTemplate(string name, PropertyDescriptor descriptor, Binding binding)
		{
			if (linkCellTemplates.TryGetValue(name, out var template))
			{
				return template;
			}

			var tb = new FrameworkElementFactory(typeof(TextBlock));
			var hyper = new FrameworkElementFactory(typeof(Hyperlink));
			tb.AppendChild(hyper);
			hyper.AddHandler(Hyperlink.ClickEvent, new RoutedEventHandler(Hyperlink_Click));
			var run = new FrameworkElementFactory(typeof(Run));
			hyper.AppendChild(run);
			run.SetBinding(Run.TextProperty, binding);

			DataTemplate dataTemplate = new DataTemplate() { VisualTree = tb };
			linkCellTemplates.Add(name, dataTemplate);
			return dataTemplate;

			void Hyperlink_Click(object sender, RoutedEventArgs e)
			{
				var hyperlink = (Hyperlink)sender;
				var onClickMethod = descriptor.ComponentType.GetMethod("On" + name + "Click", BindingFlags.Instance | BindingFlags.Public);
				if (onClickMethod != null)
				{
					onClickMethod.Invoke(hyperlink.DataContext, Array.Empty<object>());
				}
			}
		}

		static void ApplyAttributes(PropertyDescriptor descriptor, Binding binding, DataGridColumn column)
		{
			if (descriptor.PropertyType.IsEnum)
			{
				binding.Converter = new UnderlyingEnumValueConverter();
				string key = descriptor.PropertyType.Name + "Filter";
				column.SetTemplate((ControlTemplate)MetadataTableViews.Instance[key]);
			}
			var columnInfo = descriptor.Attributes.OfType<ColumnInfoAttribute>().FirstOrDefault();
			if (columnInfo != null)
			{
				binding.StringFormat = columnInfo.Format;
				if (!descriptor.PropertyType.IsEnum
					&& columnInfo.Format.StartsWith("X", StringComparison.OrdinalIgnoreCase))
				{
					column.SetTemplate((ControlTemplate)MetadataTableViews.Instance["HexFilter"]);
				}
			}
		}

		[Obsolete("Use safe GetValueLittleEndian(ReadOnlySpan<byte>) or appropriate BinaryPrimitives.Read* method")]
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static unsafe int GetValue(byte* ptr, int size)
			=> GetValueLittleEndian(new ReadOnlySpan<byte>(ptr, size));

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static int GetValueLittleEndian(ReadOnlySpan<byte> ptr, int size)
			=> GetValueLittleEndian(ptr.Slice(0, size));

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static int GetValueLittleEndian(ReadOnlySpan<byte> ptr)
		{
			int result = 0;
			for (int i = 0; i < ptr.Length; i += 2)
			{
				result |= ptr[i] << 8 * i;
				result |= ptr[i + 1] << 8 * (i + 1);
			}
			return result;
		}

		static Helpers()
		{
			rowCounts = typeof(MetadataReader)
				.GetField("TableRowCounts", BindingFlags.NonPublic | BindingFlags.Instance);
			computeCodedTokenSize = typeof(MetadataReader)
				.GetMethod("ComputeCodedTokenSize", BindingFlags.Instance | BindingFlags.NonPublic);
			fromTypeDefOrRefTag = typeof(TypeDefinitionHandle).Assembly
				.GetType("System.Reflection.Metadata.Ecma335.TypeDefOrRefTag")
				.GetMethod("ConvertToHandle", BindingFlags.Static | BindingFlags.NonPublic);
			fromHasFieldMarshalTag = typeof(TypeDefinitionHandle).Assembly
				.GetType("System.Reflection.Metadata.Ecma335.HasFieldMarshalTag")
				.GetMethod("ConvertToHandle", BindingFlags.Static | BindingFlags.NonPublic);
			fromMemberForwardedTag = typeof(TypeDefinitionHandle).Assembly
				.GetType("System.Reflection.Metadata.Ecma335.MemberForwardedTag")
				.GetMethod("ConvertToHandle", BindingFlags.Static | BindingFlags.NonPublic);
		}

		readonly static FieldInfo rowCounts;
		readonly static MethodInfo computeCodedTokenSize;
		readonly static MethodInfo fromTypeDefOrRefTag;
		readonly static MethodInfo fromHasFieldMarshalTag;
		readonly static MethodInfo fromMemberForwardedTag;

		public static EntityHandle FromHasFieldMarshalTag(uint tag)
		{
			return (EntityHandle)fromHasFieldMarshalTag.Invoke(null, new object[] { tag });
		}

		public static EntityHandle FromMemberForwardedTag(uint tag)
		{
			return (EntityHandle)fromMemberForwardedTag.Invoke(null, new object[] { tag });
		}

		public static EntityHandle FromTypeDefOrRefTag(uint tag)
		{
			return (EntityHandle)fromTypeDefOrRefTag.Invoke(null, new object[] { tag });
		}

		public static int ComputeCodedTokenSize(this MetadataReader metadata, int largeRowSize,
			TableMask mask)
		{
			return (int)computeCodedTokenSize.Invoke(metadata, new object[] {
				largeRowSize, rowCounts.GetValue(metadata), (ulong)mask }
			);
		}

		class UnderlyingEnumValueConverter : IValueConverter
		{
			public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
			{
				var t = value.GetType();
				if (t.IsEnum)
				{
					return (int)CSharpPrimitiveCast.Cast(TypeCode.Int32, value, false);
				}
				return value;
			}

			public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
			{
				throw new NotImplementedException();
			}
		}

		public static string ReadUTF8StringNullTerminated(this ref BlobReader reader)
		{
			int length = reader.IndexOf(0);
			string s = reader.ReadUTF8(length);
			reader.ReadByte();
			return s;
		}
	}

	enum ColumnKind
	{
		HeapOffset,
		Token,
		Other
	}

	[AttributeUsage(AttributeTargets.Property)]
	class ColumnInfoAttribute : Attribute
	{
		public string Format { get; }

		public ColumnKind Kind { get; set; }

		public bool LinkToTable { get; set; }

		public ColumnInfoAttribute(string format)
		{
			this.Format = format;
		}
	}

	[Flags]
	internal enum TableMask : ulong
	{
		Module = 0x1,
		TypeRef = 0x2,
		TypeDef = 0x4,
		FieldPtr = 0x8,
		Field = 0x10,
		MethodPtr = 0x20,
		MethodDef = 0x40,
		ParamPtr = 0x80,
		Param = 0x100,
		InterfaceImpl = 0x200,
		MemberRef = 0x400,
		Constant = 0x800,
		CustomAttribute = 0x1000,
		FieldMarshal = 0x2000,
		DeclSecurity = 0x4000,
		ClassLayout = 0x8000,
		FieldLayout = 0x10000,
		StandAloneSig = 0x20000,
		EventMap = 0x40000,
		EventPtr = 0x80000,
		Event = 0x100000,
		PropertyMap = 0x200000,
		PropertyPtr = 0x400000,
		Property = 0x800000,
		MethodSemantics = 0x1000000,
		MethodImpl = 0x2000000,
		ModuleRef = 0x4000000,
		TypeSpec = 0x8000000,
		ImplMap = 0x10000000,
		FieldRva = 0x20000000,
		EnCLog = 0x40000000,
		EnCMap = 0x80000000,
		Assembly = 0x100000000,
		AssemblyRef = 0x800000000,
		File = 0x4000000000,
		ExportedType = 0x8000000000,
		ManifestResource = 0x10000000000,
		NestedClass = 0x20000000000,
		GenericParam = 0x40000000000,
		MethodSpec = 0x80000000000,
		GenericParamConstraint = 0x100000000000,
		Document = 0x1000000000000,
		MethodDebugInformation = 0x2000000000000,
		LocalScope = 0x4000000000000,
		LocalVariable = 0x8000000000000,
		LocalConstant = 0x10000000000000,
		ImportScope = 0x20000000000000,
		StateMachineMethod = 0x40000000000000,
		CustomDebugInformation = 0x80000000000000,
		PtrTables = 0x4800A8,
		EncTables = 0xC0000000,
		TypeSystemTables = 0x1FC9FFFFFFFF,
		DebugTables = 0xFF000000000000,
		AllTables = 0xFF1FC9FFFFFFFF,
		ValidPortablePdbExternalTables = 0x1FC93FB7FF57
	}
}
