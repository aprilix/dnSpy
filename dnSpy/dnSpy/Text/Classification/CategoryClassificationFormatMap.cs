﻿/*
    Copyright (C) 2014-2016 de4dot@gmail.com

    This file is part of dnSpy

    dnSpy is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    dnSpy is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with dnSpy.  If not, see <http://www.gnu.org/licenses/>.
*/

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using dnSpy.Contracts.Text.Classification;
using dnSpy.Contracts.Themes;
using dnSpy.Text.MEF;
using Microsoft.VisualStudio.Text.Classification;
using Microsoft.VisualStudio.Text.Formatting;

namespace dnSpy.Text.Classification {
	sealed class CategoryClassificationFormatMap : IClassificationFormatMap {
		public TextFormattingRunProperties DefaultTextProperties {
			get {
				Debug.Assert(defaultTextFormattingRunProperties != null);
				return defaultTextFormattingRunProperties;
			}
			set {
				if (value == null)
					return;
				if (value == defaultTextFormattingRunProperties)
					return;
				defaultTextFormattingRunProperties = value;
				ClassificationFontUtils.CopyTo(defaultResourceDictionary, defaultTextFormattingRunProperties);
				defaultTextFormattingRunProperties = ClassificationFontUtils.Create(defaultResourceDictionary);
				ClassificationFormatMappingChanged?.Invoke(this, EventArgs.Empty);
			}
		}

		public event EventHandler<EventArgs> ClassificationFormatMappingChanged;

		public ReadOnlyCollection<IClassificationType> CurrentPriorityOrder {
			get {
				throw new NotImplementedException();//TODO:
			}
		}

		public bool IsInBatchUpdate {
			get {
				throw new NotImplementedException();//TODO:
			}
		}

		readonly IThemeManager themeManager;
		readonly IEditorFormatMap editorFormatMap;
		readonly Dictionary<IClassificationType, ClassificationInfo> toClassificationInfo;
		readonly Dictionary<IClassificationType, Lazy<EditorFormatDefinition, IClassificationFormatMetadata>> toEditorFormatDefinition;
		readonly Dictionary<IClassificationType, int> toClassificationTypeOrder;
		readonly Dictionary<string, string> classificationToEditorFormatMapKey;
		readonly ResourceDictionary defaultResourceDictionary;
		TextFormattingRunProperties defaultTextFormattingRunProperties;

		sealed class ClassificationInfo {
			public ResourceDictionary ExplicitResourceDictionary { get; set; }
			public ResourceDictionary InheritedResourceDictionary { get; set; }
			public TextFormattingRunProperties ExplicitTextProperties { get; set; }
			public TextFormattingRunProperties InheritedTextProperties { get; set; }
			public Lazy<EditorFormatDefinition, IClassificationFormatMetadata> Lazy { get; }
			public IClassificationType ClassificationType { get; }

			public ClassificationInfo(Lazy<EditorFormatDefinition, IClassificationFormatMetadata> lazy, IClassificationType classificationType) {
				Lazy = lazy;
				ClassificationType = classificationType;
			}
		}

		public CategoryClassificationFormatMap(IThemeManager themeManager, IEditorFormatMap editorFormatMap, IEditorFormatDefinitionService editorFormatDefinitionService, IClassificationTypeRegistryService classificationTypeRegistryService) {
			if (themeManager == null)
				throw new ArgumentNullException(nameof(themeManager));
			if (editorFormatMap == null)
				throw new ArgumentNullException(nameof(editorFormatMap));
			if (editorFormatDefinitionService == null)
				throw new ArgumentNullException(nameof(editorFormatDefinitionService));
			if (classificationTypeRegistryService == null)
				throw new ArgumentNullException(nameof(classificationTypeRegistryService));
			this.themeManager = themeManager;
			this.editorFormatMap = editorFormatMap;
			this.toClassificationInfo = new Dictionary<IClassificationType, ClassificationInfo>();
			this.toEditorFormatDefinition = new Dictionary<IClassificationType, Lazy<EditorFormatDefinition, IClassificationFormatMetadata>>(editorFormatDefinitionService.ClassificationFormatDefinitions.Length);
			this.toClassificationTypeOrder = new Dictionary<IClassificationType, int>();
			this.classificationToEditorFormatMapKey = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			this.defaultResourceDictionary = new ResourceDictionary();

			for (int i = 0; i < editorFormatDefinitionService.ClassificationFormatDefinitions.Length; i++) {
				var e = editorFormatDefinitionService.ClassificationFormatDefinitions[i];
				foreach (var ctString in e.Metadata.ClassificationTypeNames) {
					var classificationType = classificationTypeRegistryService.GetClassificationType(ctString);
					Debug.Assert(classificationType != null);
					if (classificationType == null)
						continue;
					Debug.Assert(!toEditorFormatDefinition.ContainsKey(classificationType));
					if (!toEditorFormatDefinition.ContainsKey(classificationType)) {
						toClassificationTypeOrder.Add(classificationType, toClassificationTypeOrder.Count);
						toEditorFormatDefinition.Add(classificationType, e);
						classificationToEditorFormatMapKey.Add(classificationType.Classification, ((IEditorFormatMetadata)e.Metadata).Name);
					}
				}
			}

			editorFormatMap.FormatMappingChanged += EditorFormatMap_FormatMappingChanged;

			ReinitializeCache();
		}

		void EditorFormatMap_FormatMappingChanged(object sender, FormatItemsEventArgs e) {
			ReinitializeCache();
			ClassificationFormatMappingChanged?.Invoke(this, EventArgs.Empty);
		}

		void ReinitializeCache() {
			toClassificationInfo.Clear();
			ClassificationFontUtils.CopyTo(defaultResourceDictionary, editorFormatMap.GetProperties(EditorFormatMapConstants.PlainText));
			defaultTextFormattingRunProperties = ClassificationFontUtils.Create(defaultResourceDictionary);
		}

		sealed class TransientClassificationFormatDefinition : ClassificationFormatDefinition {
		}

		sealed class ClassificationFormatMetadata : IClassificationFormatMetadata {
			public IEnumerable<string> After { get; }
			public IEnumerable<string> Before { get; }
			public IEnumerable<string> ClassificationTypeNames { get; }
			public string Name { get; }
			public bool UserVisible { get; }
			public ClassificationFormatMetadata(string classification) {
				Name = classification;
				ClassificationTypeNames = new string[] { classification };
			}
		}

		ClassificationInfo TryGetClassificationInfo(IClassificationType classificationType, bool canCreate) {
			ClassificationInfo info;
			if (!toClassificationInfo.TryGetValue(classificationType, out info)) {
				Lazy<EditorFormatDefinition, IClassificationFormatMetadata> lazy;
				if (!toEditorFormatDefinition.TryGetValue(classificationType, out lazy)) {
					if (!canCreate)
						return null;
					lazy = new Lazy<EditorFormatDefinition, IClassificationFormatMetadata>(() => new TransientClassificationFormatDefinition(), new ClassificationFormatMetadata(classificationType.Classification));
					var dummy = lazy.Value;
					toEditorFormatDefinition.Add(classificationType, lazy);
				}
				toClassificationInfo.Add(classificationType, info = new ClassificationInfo(lazy, classificationType));
			}
			return info;
		}

		public TextFormattingRunProperties GetExplicitTextProperties(IClassificationType classificationType) {
			if (classificationType == null)
				throw new ArgumentNullException(nameof(classificationType));
			var info = TryGetClassificationInfo(classificationType, canCreate: false);
			if (info == null)
				return TextFormattingRunProperties.CreateTextFormattingRunProperties();
			if (info.ExplicitTextProperties == null)
				CreateExplicitTextProperties(info);
			Debug.Assert(info.ExplicitTextProperties != null);
			return info.ExplicitTextProperties;
		}

		public TextFormattingRunProperties GetTextProperties(IClassificationType classificationType) {
			if (classificationType == null)
				throw new ArgumentNullException(nameof(classificationType));
			var info = TryGetClassificationInfo(classificationType, canCreate: true);
			Debug.Assert(info != null);
			if (info.InheritedTextProperties == null)
				CreateInheritedTextProperties(info);
			Debug.Assert(info.InheritedTextProperties != null);
			return info.InheritedTextProperties;
		}

		void CreateExplicitTextProperties(ClassificationInfo info) {
			var props = info.Lazy.Value.CreateThemeResourceDictionary(themeManager.Theme);
			info.ExplicitResourceDictionary = props;
			info.ExplicitTextProperties = ClassificationFontUtils.Create(info.ExplicitResourceDictionary);
		}

		void CreateInheritedTextProperties(ClassificationInfo info) {
			var list = new List<IClassificationType>();
			AddBaseTypes(list, info.ClassificationType);
			info.InheritedResourceDictionary = CreateInheritedResourceDictionary(defaultResourceDictionary, list);
			info.InheritedTextProperties = ClassificationFontUtils.Create(info.InheritedResourceDictionary);
		}

		ResourceDictionary CreateInheritedResourceDictionary(ResourceDictionary r, List<IClassificationType> types) {
			var res = new ResourceDictionary();
			res.MergedDictionaries.Add(r);
			for (int i = types.Count - 1; i >= 0; i--) {
				var info = TryGetClassificationInfo(types[i], canCreate: false);
				if (info == null)
					continue;
				if (info.ExplicitTextProperties == null)
					CreateExplicitTextProperties(info);
				res.MergedDictionaries.Add(info.ExplicitResourceDictionary);
			}
			return res;
		}

		void AddBaseTypes(List<IClassificationType> list, IClassificationType classificationType) {
			if (list.Contains(classificationType))
				return;
			list.Add(classificationType);
			var baseTypes = classificationType.BaseTypes.ToArray();
			if (baseTypes.Length > 1)
				Array.Sort(baseTypes, classificationTypeComparer ?? (classificationTypeComparer = new ClassificationTypeComparer(this)));
			foreach (var bt in baseTypes)
				AddBaseTypes(list, bt);
		}
		ClassificationTypeComparer classificationTypeComparer;

		public string GetEditorFormatMapKey(IClassificationType classificationType) {
			if (classificationType == null)
				throw new ArgumentNullException(nameof(classificationType));
			string key;
			if (!classificationToEditorFormatMapKey.TryGetValue(classificationType.Classification, out key))
				key = classificationType.Classification;
			return key;
		}

		public void AddExplicitTextProperties(IClassificationType classificationType, TextFormattingRunProperties properties) {
			throw new NotImplementedException();//TODO:
		}

		public void AddExplicitTextProperties(IClassificationType classificationType, TextFormattingRunProperties properties, IClassificationType priority) {
			throw new NotImplementedException();//TODO:
		}

		public void SetTextProperties(IClassificationType classificationType, TextFormattingRunProperties properties) {
			throw new NotImplementedException();//TODO:
		}

		public void SetExplicitTextProperties(IClassificationType classificationType, TextFormattingRunProperties properties) {
			throw new NotImplementedException();//TODO:
		}

		public void SwapPriorities(IClassificationType firstType, IClassificationType secondType) {
			throw new NotImplementedException();//TODO:
		}

		public void BeginBatchUpdate() {
			throw new NotImplementedException();//TODO:
		}

		public void EndBatchUpdate() {
			throw new NotImplementedException();//TODO:
		}

		sealed class ClassificationTypeComparer : IComparer<IClassificationType> {
			readonly CategoryClassificationFormatMap owner;

			public ClassificationTypeComparer(CategoryClassificationFormatMap owner) {
				this.owner = owner;
			}

			public int Compare(IClassificationType x, IClassificationType y) => GetOrder(y) - GetOrder(x);

			int GetOrder(IClassificationType a) {
				if (a == null)
					return -1;
				int order;
				if (owner.toClassificationTypeOrder.TryGetValue(a, out order))
					return order;
				return -1;
			}
		}
	}
}
