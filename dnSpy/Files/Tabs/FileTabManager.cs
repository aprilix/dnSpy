﻿/*
    Copyright (C) 2014-2015 de4dot@gmail.com

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
using System.ComponentModel.Composition;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using dnSpy.Contracts.Files.Tabs;
using dnSpy.Contracts.Files.TreeView;
using dnSpy.Contracts.Menus;
using dnSpy.Contracts.Tabs;
using dnSpy.Contracts.TreeView;
using dnSpy.Files.TreeView;

namespace dnSpy.Files.Tabs {
	[Export, Export(typeof(IFileTabManager)), PartCreationPolicy(CreationPolicy.Shared)]
	sealed class FileTabManager : IFileTabManager {
		IFileTreeView IFileTabManager.FileTreeView {
			get { return fileTreeView; }
		}
		public FileTreeView FileTreeView {
			get { return fileTreeView; }
		}
		readonly FileTreeView fileTreeView;

		public ITabGroupManager TabGroupManager {
			get { return tabGroupManager; }
		}
		readonly ITabGroupManager tabGroupManager;

		IEnumerable<TabContentImpl> AllTabContentImpls {
			get {
				foreach (var g in TabGroupManager.TabGroups) {
					foreach (TabContentImpl impl in g.TabContents)
						yield return impl;
				}
			}
		}

		ITabGroup SafeActiveTabGroup {
			get {
				var g = TabGroupManager.ActiveTabGroup;
				if (g != null)
					return g;
				return TabGroupManager.Create();
			}
		}

		TabContentImpl ActiveTabContentImpl {
			get {
				var g = TabGroupManager.ActiveTabGroup;
				return g == null ? null : (TabContentImpl)g.ActiveTabContent;
			}
		}

		TabContentImpl SafeActiveTabContentImpl {
			get {
				var g = SafeActiveTabGroup;
				var impl = (TabContentImpl)g.ActiveTabContent;
				if (impl != null)
					return impl;
				return CreateNewTab(g);
			}
		}

		TabContentImpl CreateNewTab(ITabGroup tabGroup) {
			var impl = new TabContentImpl(this, fileTabUIContextLocatorCreator.Create(), refFactories);
			tabGroup.Add(impl);
			return impl;
		}

		IFileTab IFileTabManager.ActiveTab {
			get { return ActiveTabContentImpl; }
			set {
				if (value == null)
					throw new ArgumentNullException();
				var impl = value as TabContentImpl;
				if (impl == null)
					throw new InvalidOperationException();
				var g = GetTabGroup(impl);
				if (g == null)
					throw new InvalidOperationException();
				g.ActiveTabContent = impl;
				TabGroupManager.ActiveTabGroup = g;
			}
		}

		ITabGroup GetTabGroup(TabContentImpl impl) {
			foreach (var g in TabGroupManager.TabGroups) {
				if (g.TabContents.Contains(impl))
					return g;
			}
			return null;
		}

		IFileTab IFileTabManager.GetOrCreateActiveTab() {
			return SafeActiveTabContentImpl;
		}

		public IEnumerable<IFileTab> SortedTabs {
			get {
				var groups = TabGroupManager.TabGroups.ToArray();
				if (groups.Length == 0)
					yield break;
				int startGroupIndex = Array.IndexOf(groups, TabGroupManager.ActiveTabGroup);
				Debug.Assert(startGroupIndex >= 0);
				for (int i = 0; i < groups.Length; i++) {
					var g = groups[(startGroupIndex + i) % groups.Length];

					var contents = g.TabContents.Cast<TabContentImpl>().ToArray();
					if (contents.Length == 0)
						continue;
					int startContentIndex = Array.IndexOf(contents, g.ActiveTabContent);
					Debug.Assert(startContentIndex >= 0);
					for (int j = 0; j < contents.Length; j++) {
						var c = contents[(startContentIndex + j) % contents.Length];
						yield return c;
					}
				}
			}
		}

		public IEnumerable<IFileTab> VisibleFirstTabs {
			get {
				var hash = new HashSet<IFileTab>();
				foreach (var g in TabGroupManager.TabGroups) {
					var c = (TabContentImpl)g.ActiveTabContent;
					hash.Add(c);
					yield return c;
				}
				foreach (var c in SortedTabs) {
					if (!hash.Contains(c))
						yield return c;
				}
			}
		}

		public IFileTabManagerSettings Settings {
			get { return fileTabManagerSettings; }
		}
		readonly IFileTabManagerSettings fileTabManagerSettings;

		readonly IFileTabUIContextLocatorCreator fileTabUIContextLocatorCreator;
		readonly ITabManager tabManager;
		readonly IFileTabContentFactoryManager fileTabContentFactoryManager;
		readonly Lazy<IReferenceFileTabContentCreator, IReferenceFileTabContentCreatorMetadata>[] refFactories;

		[ImportingConstructor]
		FileTabManager(IFileTabUIContextLocatorCreator fileTabUIContextLocatorCreator, FileTreeView fileTreeView, ITabManagerCreator tabManagerCreator, IFileTabContentFactoryManager fileTabContentFactoryManager, IFileTabManagerSettings fileTabManagerSettings, [ImportMany] IEnumerable<Lazy<IReferenceFileTabContentCreator, IReferenceFileTabContentCreatorMetadata>> mefRefFactories) {
			this.fileTabManagerSettings = fileTabManagerSettings;
			this.fileTabUIContextLocatorCreator = fileTabUIContextLocatorCreator;
			this.fileTabContentFactoryManager = fileTabContentFactoryManager;
			this.refFactories = mefRefFactories.OrderBy(a => a.Metadata.Order).ToArray();
			this.fileTreeView = fileTreeView;
			this.fileTreeView.TreeView.SelectionChanged += TreeView_SelectionChanged;
			this.fileTreeView.NodesTextChanged += FileTreeView_NodesTextChanged;
			this.tabManager = tabManagerCreator.Create();
			this.tabGroupManager = this.tabManager.Create(new Guid(MenuConstants.GUIDOBJ_FILES_TABCONTROL_GUID));
			this.tabGroupManager.TabSelectionChanged += TabGroupManager_TabSelectionChanged;
			this.tabGroupManager.TabGroupSelectionChanged += TabGroupManager_TabGroupSelectionChanged;
			this.tabGroupManager.Create();
		}

		void TabGroupManager_TabGroupSelectionChanged(object sender, TabGroupSelectedEventArgs e) {
			if (e.Unselected != null) {
				var impl = (TabContentImpl)e.Unselected.ActiveTabContent;
				if (impl != null)
					impl.OnUnselected();
			}
			if (e.Selected != null) {
				var impl = (TabContentImpl)e.Selected.ActiveTabContent;
				if (impl != null) {
					impl.OnSelected();
					OnNewTabContentShown(impl);
				}
			}
		}

		void TabGroupManager_TabSelectionChanged(object sender, TabSelectedEventArgs e) {
			if (e.Unselected != null) {
				var impl = (TabContentImpl)e.Unselected;
				impl.OnUnselected();
			}
			if (e.Selected != null) {
				Debug.Assert(e.TabGroup.ActiveTabContent == e.Selected);
				e.TabGroup.SetFocus(e.Selected);
				var impl = (TabContentImpl)e.Selected;
				impl.OnSelected();
				OnNewTabContentShown(impl);
			}
		}

		void FileTreeView_NodesTextChanged(object sender, EventArgs e) {
			foreach (var impl in AllTabContentImpls)
				impl.UpdateTitleAndToolTip();
		}

		void TreeView_SelectionChanged(object sender, TVSelectionChangedEventArgs e) {
			if (disableSelectionChangedEventCounter > 0)
				return;
			var nodes = ((ITreeView)sender).TopLevelSelection.OfType<IFileTreeNodeData>().ToArray();

			// When the treeview selects nodes it will unselect everything and then select the new
			// nodes. We're not interested in the empty selection since it shouldn't be recorded in
			// the navigation history. If we get an empty selection, it could be because of the
			// treeview or it's because the user unselected everything. Show the empty nodes with
			// a slight delay so we can cancel it if the real selection immediately follows.
			// Reproduce: Select a node, then collapse its parent to unselect the node and select
			// the parent. Or open a new file.
			if (nodes.Length != 0 || inEmptySelectionHack) {
				ignoreEmptySelection = true;
				ShowNodes(nodes);
			}
			else {
				inEmptySelectionHack = true;
				ignoreEmptySelection = false;
				Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Send, new Action(() => {
					var old = ignoreEmptySelection;
					inEmptySelectionHack = false;
					ignoreEmptySelection = false;
					if (!old)
						ShowNodes(nodes);
				}));
			}
		}
		int disableSelectionChangedEventCounter = 0;
		bool inEmptySelectionHack;
		bool ignoreEmptySelection;

		void ShowNodes(IFileTreeNodeData[] nodes) {
			var tabContent = CreateTabContent(nodes);
			disableSelectTreeNodes++;
			try {
				SafeActiveTabContentImpl.Show(tabContent, null, null);
			}
			finally {
				disableSelectTreeNodes--;
			}
		}

		IFileTabContent CreateTabContent(IFileTreeNodeData[] nodes) {
			var content = fileTabContentFactoryManager.CreateTabContent(nodes);
			Debug.Assert(content != null);
			return content ?? new NullFileTabContent();
		}

		internal void Add(ITabGroup group, IFileTabContent tabContent, object serializedUI, Action<ShowTabContentEventArgs> onShown) {
			Debug.Assert(TabGroupManager.TabGroups.Contains(group));
			var tab = OpenEmptyTab(group);
			tab.Show(tabContent, serializedUI, onShown);
		}

		public IFileTab OpenEmptyTab() {
			return OpenEmptyTab(SafeActiveTabGroup);
		}

		IFileTab OpenEmptyTab(ITabGroup g) {
			var impl = CreateNewTab(g);
			g.ActiveTabContent = impl;
			return impl;
		}

		int disableSelectTreeNodes;
		internal void OnNewTabContentShown(IFileTab fileTab) {
			if (!tabsLoaded)
				return;
			if (disableSelectTreeNodes > 0)
				return;
			if (fileTab != ActiveTabContentImpl)
				return;
			Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => {
				if (fileTab == ActiveTabContentImpl)
					OnNewTabContentShownDelay(fileTab);
			}));
		}

		void OnNewTabContentShownDelay(IFileTab fileTab) {
			var newNodes = fileTab.Content.Nodes.ToArray();
			if (Equals(fileTreeView.TreeView.SelectedItems, newNodes))
				return;

			// The treeview steals the focus so remember the current focused element. Don't restore
			// the focus if it's a node in the treeview.
			var focusedElem = Keyboard.FocusedElement;
			if (((UIElement)fileTreeView.TreeView.UIObject).IsKeyboardFocusWithin)
				focusedElem = null;
			bool tabGroupHasFocus = tabGroupManager.TabGroups.Any(a => a.IsKeyboardFocusWithin);

			disableSelectionChangedEventCounter++;
			try {
				fileTreeView.TreeView.SelectItems(newNodes);
			}
			finally {
				disableSelectionChangedEventCounter--;
			}

			if (focusedElem != null && Keyboard.FocusedElement != focusedElem) {
				if (tabGroupHasFocus) {
					var tab = ActiveTabContentImpl;
					Debug.Assert(tab != null);
					if (tab != null)
						tab.TrySetFocus();
				}
				else
					focusedElem.Focus();
			}
		}

		static bool Equals(ITreeNodeData[] a, ITreeNodeData[] b) {
			if (a == b)
				return true;
			if (a == null || b == null)
				return false;
			if (a.Length != b.Length)
				return false;
			for (int i = 0; i < a.Length; i++) {
				if (a[i] != b[i])
					return false;
			}
			return true;
		}

		public void SetFocus(IFileTab tab) {
			if (tab == null)
				throw new ArgumentNullException();
			var impl = tab as TabContentImpl;
			if (impl == null)
				throw new InvalidOperationException();
			var g = GetTabGroup(impl);
			if (g == null)
				throw new InvalidOperationException();
			g.SetFocus(impl);
		}

		public void ForceRefresh(IEnumerable<IFileTab> tabs) {
			if (tabs == null)
				throw new ArgumentNullException();
			foreach (var tab in tabs.ToArray()) {
				var impl = tab as TabContentImpl;
				if (impl == null)
					throw new InvalidOperationException();
				impl.Refresh();
			}
		}

		public bool Owns(ITabGroup tabGroup) {
			return tabGroupManager.TabGroups.Contains(tabGroup);
		}

		internal void OnTabsLoaded() {
			Debug.Assert(!tabsLoaded);
			tabsLoaded = true;
			var impl = ActiveTabContentImpl;
			if (impl != null) {
				impl.OnTabsLoaded();
				OnNewTabContentShown(impl);
			}
		}
		bool tabsLoaded = false;

		public void Close(IFileTab tab) {
			if (tab == null)
				throw new ArgumentNullException();
			var impl = tab as TabContentImpl;
			if (impl == null)
				throw new InvalidOperationException();
			var g = GetTabGroup(impl);
			if (g == null)
				throw new InvalidOperationException();
			g.Close(impl);
		}

		public IFileTab TryGetFileTab(ITabContent content) {
			var impl = content as TabContentImpl;
			if (impl == null)
				return null;
			return GetTabGroup(impl) == null ? null : impl;
		}
	}
}
