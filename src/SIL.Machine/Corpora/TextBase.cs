﻿using System;
using System.Collections.Generic;
using System.Linq;
using SIL.Machine.Tokenization;
using SIL.Machine.Translation;

namespace SIL.Machine.Corpora
{
	public abstract class TextBase : IText
	{
		protected TextBase(ITokenizer<string, int, string> wordTokenizer, string id, string sortKey)
		{
			WordTokenizer = wordTokenizer;
			Id = id;
			SortKey = sortKey;
		}

		public string Id { get; }

		public string SortKey { get; }

		protected ITokenizer<string, int, string> WordTokenizer { get; }

		public abstract IEnumerable<TextSegment> GetSegments(bool includeText = true);

		protected TextSegment CreateTextSegment(bool includeText, string text, object segRef,
			bool isSentenceStart = true, bool isInRange = false, bool isRangeStart = false)
		{
			text = text.Trim();
			if (!includeText)
			{
				return new TextSegment(Id, segRef, Array.Empty<string>(), isSentenceStart, isInRange, isRangeStart,
					isEmpty: text.Length == 0);
			}
			IReadOnlyList<string> segment = WordTokenizer.Tokenize(text).ToArray();
			segment = TokenProcessors.UnescapeSpaces.Process(segment);
			return new TextSegment(Id, segRef, segment, isSentenceStart, isInRange, isRangeStart, segment.Count == 0);
		}

		protected TextSegment CreateTextSegment(object segRef, bool isInRange = false)
		{
			return new TextSegment(Id, segRef, Array.Empty<string>(), isSentenceStart: true, isInRange,
				isRangeStart: false, isEmpty: true);
		}

		protected TextSegment CreateTextSegment(bool includeText, string text, params int[] indices)
		{
			return CreateTextSegment(includeText, text, new TextSegmentRef(indices));
		}
	}
}
