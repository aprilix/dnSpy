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
using System.Diagnostics;
using Microsoft.VisualStudio.Text;

namespace dnSpy.Contracts.Language.Intellisense {
	/// <summary>
	/// Default <see cref="Completion"/> filter
	/// </summary>
	sealed class CompletionFilter : ICompletionFilter {
		readonly string searchText;
		readonly int[] acronymMatchIndexes;
		const StringComparison stringComparison = StringComparison.CurrentCultureIgnoreCase;

		/// <summary>
		/// Constructor
		/// </summary>
		/// <param name="searchText">Search text</param>
		public CompletionFilter(string searchText) {
			if (searchText == null)
				throw new ArgumentNullException(nameof(searchText));
			this.searchText = searchText;
			this.acronymMatchIndexes = AcronymSearchHelpers.TryCreateMatchIndexes(searchText);
		}

		bool TryUpdateAcronymIndexes(string completionText) =>
			AcronymSearchHelpers.TryUpdateAcronymIndexes(acronymMatchIndexes, searchText, completionText);

		public bool IsMatch(Completion completion) {
			var completionText = completion.FilterText;

			if (completionText.IndexOf(searchText, stringComparison) >= 0)
				return true;
			if (acronymMatchIndexes != null && TryUpdateAcronymIndexes(completionText))
				return true;

			return false;
		}

		public IEnumerable<Span> GetMatchSpans(Completion completion, string completionText) {
			Debug.Assert(acronymMatchIndexes == null || acronymMatchIndexes.Length > 0);

			// Acronyms have higher priority, eg. TA should match |T|ask|A|waiter
			// and not |Ta|skAwaiter.
			if (acronymMatchIndexes != null && TryUpdateAcronymIndexes(completionText)) {
				foreach (var i in acronymMatchIndexes)
					yield return new Span(i, 1);
				yield break;
			}

			int index = completionText.IndexOf(searchText, stringComparison);
			if (index >= 0) {
				yield return new Span(index, searchText.Length);
				yield break;
			}
		}
	}
}
