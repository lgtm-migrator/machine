﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SIL.Collections;
using SIL.Machine.Fsa;

namespace SIL.Machine.Matching
{
	public class Matcher<TData, TOffset> where TData : IData<TOffset>
	{
		public const string EntireMatch = "*entire*";

		private static readonly IEqualityComparer<Match<TData, TOffset>> MatchComparer = AnonymousEqualityComparer.Create<Match<TData, TOffset>>(MatchEquals, MatchGetHashCode); 

		private static bool MatchEquals(Match<TData, TOffset> x, Match<TData, TOffset> y)
		{
			return EqualityComparer<TOffset>.Default.Equals(x.Span.Start, y.Span.Start) && EqualityComparer<TOffset>.Default.Equals(x.Span.End, y.Span.End)
				&& x.PatternPath.SequenceEqual(y.PatternPath);
		}

		private static int MatchGetHashCode(Match<TData, TOffset> m)
		{
			int code = 23;
			code = code * 31 + (m.Span.Start == null ? 0 : EqualityComparer<TOffset>.Default.GetHashCode(m.Span.Start));
			code = code * 31 + (m.Span.End == null ? 0 : EqualityComparer<TOffset>.Default.GetHashCode(m.Span.End));
			code = code * 31 + m.PatternPath.GetSequenceHashCode();
			return code;
		}

		private readonly SpanFactory<TOffset> _spanFactory;
		private readonly MatcherSettings<TOffset> _settings;
		private readonly FiniteStateAutomaton<TData, TOffset> _fsa;

		public Matcher(SpanFactory<TOffset> spanFactory, Pattern<TData, TOffset> pattern)
			: this(spanFactory, pattern, new MatcherSettings<TOffset>())
		{
		}

		public Matcher(SpanFactory<TOffset> spanFactory, Pattern<TData, TOffset> pattern, MatcherSettings<TOffset> settings)
		{
			_spanFactory = spanFactory;
			_settings = settings;
			_settings.ReadOnly = true;
			_fsa = new FiniteStateAutomaton<TData, TOffset>(_settings.Direction, _settings.Filter);
			Compile(pattern);
		}

		public MatcherSettings<TOffset> Settings
		{
			get { return _settings; }
		}

		public Direction Direction
		{
			get { return _settings.Direction; }
		}

		private void Compile(Pattern<TData, TOffset> pattern)
		{
			int nextPriority = 0;
			bool deterministic = GeneratePatternNfa(_fsa.StartState, pattern, null, new Func<Match<TData, TOffset>, bool>[0], ref nextPriority);

			var writer = new StreamWriter(string.Format("c:\\{0}-nfa.dot", _settings.Direction == Direction.LeftToRight ? "ltor" : "rtol"));
			_fsa.ToGraphViz(writer);
			writer.Close();

			if (deterministic && !_settings.AllSubmatches)
			{
				_fsa.Determinize(_settings.FastCompile);

				writer = new StreamWriter(string.Format("c:\\{0}-dfa.dot", _settings.Direction == Direction.LeftToRight ? "ltor" : "rtol"));
				_fsa.ToGraphViz(writer);
				writer.Close();
			}
		}

		private bool GeneratePatternNfa(State<TData, TOffset> startState, Pattern<TData, TOffset> pattern, string parentName,
			Func<Match<TData, TOffset>, bool>[] acceptables, ref int nextPriority)
		{
			bool deterministic = true;
			string name = parentName == null ? pattern.Name : parentName + "*" + pattern.Name;
			acceptables = acceptables.Concat(pattern.Acceptable).ToArray();
			if (pattern.Children.All(node => node is Pattern<TData, TOffset>))
			{
				foreach (Pattern<TData, TOffset> childExpr in pattern.Children)
				{
					if (!GeneratePatternNfa(startState, childExpr, name, acceptables, ref nextPriority))
						deterministic = false;
				}
			}
			else
			{
				startState = _fsa.CreateTag(startState, _fsa.CreateState(), EntireMatch, true);
				bool hasVariables;
				startState = pattern.GenerateNfa(_fsa, startState, out hasVariables);
				if (hasVariables)
					deterministic = false;
				startState = _fsa.CreateTag(startState, _fsa.CreateState(), EntireMatch, false);
				State<TData, TOffset> acceptingState = _fsa.CreateAcceptingState(name,
					(input, match) =>
					{
						Match<TData, TOffset> patMatch = CreatePatternMatch(input, match);
						return acceptables.All(acceptable => acceptable(patMatch));
					}, nextPriority++);
				startState.Arcs.Add(acceptingState);
			}

			return deterministic;
		}

		private Match<TData, TOffset> CreatePatternMatch(TData input, FsaMatch<TOffset> match)
		{
			TOffset matchStart, matchEnd;
			_fsa.GetOffsets(EntireMatch, match.Registers, out matchStart, out matchEnd);
			Span<TOffset> matchSpan = _spanFactory.Create(matchStart, matchEnd);
			var groupCaptures = new List<GroupCapture<TOffset>>();
			foreach (string groupName in _fsa.GroupNames)
			{
				if (groupName == EntireMatch)
					continue;

				GroupCapture<TOffset> groupCapture = null;
				TOffset start, end;
				if (_fsa.GetOffsets(groupName, match.Registers, out start, out end))
				{
					if (_spanFactory.IsValidSpan(start, end) && _spanFactory.CalcLength(start, end) > 0)
					{
						Span<TOffset> span = _spanFactory.Create(start, end);
						if (matchSpan.Contains(span))
							groupCapture = new GroupCapture<TOffset>(groupName, span);
					}
				}

				if (groupCapture == null)
					groupCapture = new GroupCapture<TOffset>(groupName, _spanFactory.Empty);
				groupCaptures.Add(groupCapture);
			}

			return new Match<TData, TOffset>(this, matchSpan, input, groupCaptures,
				string.IsNullOrEmpty(match.ID) ? new string[0] : match.ID.Split('*'), match.VariableBindings, match.NextAnnotation);
		}

		public bool IsMatch(TData input)
		{
			return Match(input).Success;
		}

		public bool IsMatch(TData input, TOffset start)
		{
			return Match(input, start).Success;
		}

		public Match<TData, TOffset> Match(TData input)
		{
			return Match(input, GetStartAnnotation(input));
		}

		public Match<TData, TOffset> Match(TData input, TOffset start)
		{
			return Match(input, GetStartAnnotation(input, start));
		}

		public IEnumerable<Match<TData, TOffset>> Matches(TData input)
		{
			return Matches(input, GetStartAnnotation(input));
		}

		public IEnumerable<Match<TData, TOffset>> Matches(TData input, TOffset start)
		{
			return Matches(input, GetStartAnnotation(input, start));
		}

		public IEnumerable<Match<TData, TOffset>> AllMatches(TData input)
		{
			return AllMatches(input, GetStartAnnotation(input));
		}

		public IEnumerable<Match<TData, TOffset>> AllMatches(TData input, TOffset start)
		{
			return AllMatches(input, GetStartAnnotation(input, start));
		}

		internal Match<TData, TOffset> Match(TData input, Annotation<TOffset> startAnn)
		{
			return GetMatches(input, startAnn, false).FirstOrDefault() ?? new Match<TData, TOffset>(this, _spanFactory.Empty, input);
		}

		internal IEnumerable<Match<TData, TOffset>> Matches(TData input, Annotation<TOffset> startAnn)
		{
			Match<TData, TOffset> match = Match(input, startAnn);
			while (match.Success)
			{
				yield return match;
				match = match.NextMatch();
			}
		}

		private IEnumerable<Match<TData, TOffset>> AllMatches(TData input, Annotation<TOffset> startAnn)
		{
			return GetMatches(input, startAnn, true);
		}

		private IEnumerable<Match<TData, TOffset>> GetMatches(TData input, Annotation<TOffset> startAnn, bool allMatches)
		{
			IEnumerable<FsaMatch<TOffset>> fsaMatches;
			if (_fsa.IsMatch(input, startAnn, _settings.AnchoredToStart, _settings.AnchoredToEnd, allMatches, _settings.UseDefaults, out fsaMatches))
			{
				IEnumerable<Match<TData, TOffset>> matches = fsaMatches.Select(fm => CreatePatternMatch(input, fm));
				if (!_fsa.Deterministic && !_settings.AllSubmatches)
					return matches.GroupBy(m => m, MatchComparer).Select(group => group.First());
				return matches;
			}

			return Enumerable.Empty<Match<TData, TOffset>>();
		}

		private Annotation<TOffset> GetStartAnnotation(TData input)
		{
			return GetStartAnnotation(input, input.Span.GetStart(_settings.Direction));
		}

		private Annotation<TOffset> GetStartAnnotation(TData input, TOffset start)
		{
			Annotation<TOffset> startAnn;
			if (!input.Annotations.FindDepthFirst(start, _settings.Direction, out startAnn))
				startAnn = startAnn == input.Annotations.GetBegin(_settings.Direction) ? input.Annotations.GetFirst(_settings.Direction) : startAnn.GetNext(_settings.Direction);
			if (!_settings.Filter(startAnn))
				startAnn = startAnn.GetNextDepthFirst(_settings.Direction, _settings.Filter);
			return startAnn;
		}
	}
}