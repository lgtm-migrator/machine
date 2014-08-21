﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SIL.Collections;
using SIL.Machine.Annotations;
using SIL.Machine.FeatureModel;
using SIL.Machine.Rules;

namespace SIL.HermitCrab
{
	public class Morpher
	{
		private static readonly IEqualityComparer<IEnumerable<Allomorph>> MorphsEqualityComparer = SequenceEqualityComparer.Create(ProjectionEqualityComparer<Allomorph>.Create(allo => allo.Morpheme));
		private static readonly IComparer<IEnumerable<Allomorph>> MorphsComparer = SequenceComparer.Create(ProjectionComparer<Allomorph>.Create(allo => allo.Index));

		private readonly Language _lang;
		private readonly IRule<Word, ShapeNode> _analysisRule;
		private readonly IRule<Word, ShapeNode> _synthesisRule;
		private readonly Dictionary<Stratum, RootAllomorphTrie> _allomorphTries;
		private readonly ITraceManager _traceManager;

		public Morpher(SpanFactory<ShapeNode> spanFactory, ITraceManager traceManager, Language lang)
		{
			_lang = lang;
			_traceManager = traceManager;
			_allomorphTries = new Dictionary<Stratum, RootAllomorphTrie>();
			foreach (Stratum stratum in _lang.Strata)
			{
				var allomorphs = new HashSet<RootAllomorph>(stratum.Entries.SelectMany(entry => entry.Allomorphs));
				var trie = new RootAllomorphTrie(ann => ann.Type() == HCFeatureSystem.Segment);
				foreach (RootAllomorph allomorph in allomorphs)
					trie.Add(allomorph);
				_allomorphTries[stratum] = trie;
			}
			_analysisRule = lang.CompileAnalysisRule(spanFactory, this);
			_synthesisRule = lang.CompileSynthesisRule(spanFactory, this);
			MaxStemCount = 2;
			LexEntrySelector = entry => true;
			RuleSelector = rule => true;
		}

		public ITraceManager TraceManager
		{
			get { return _traceManager; }
		}

		public int DeletionReapplications { get; set; }

		public int MaxStemCount { get; set; }

		public Func<LexEntry, bool> LexEntrySelector { get; set; }
		public Func<IHCRule, bool> RuleSelector { get; set; }

		public Language Language
		{
			get { return _lang; }
		}

		/// <summary>
		/// Morphs the specified word.
		/// </summary>
		/// <param name="word">The word.</param>
		/// <returns>All valid word synthesis records.</returns>
		public IEnumerable<Word> ParseWord(string word)
		{
			object trace;
			return ParseWord(word, out trace);
		}

		public IEnumerable<Word> ParseWord(string word, out object trace)
		{
			// convert the word to its phonetic shape
			Shape shape = _lang.SurfaceStratum.SymbolTable.Segment(word);

			var input = new Word(_lang.SurfaceStratum, shape);
			input.Freeze();
			_traceManager.AnalyzeWord(_lang, input);
			trace = input.CurrentTrace;

			// Unapply rules
			var validWordsStack = new ConcurrentStack<Word>();
			IEnumerable<Word> analyses = _analysisRule.Apply(input);

			Exception exception = null;
			Parallel.ForEach(analyses, (analysisWord, state) =>
				{
					try
					{
						foreach (Word synthesisWord in LexicalLookup(analysisWord))
						{
							Word[] valid = _synthesisRule.Apply(synthesisWord).Where(IsWordValid).ToArray();
							if (valid.Length > 0)
								validWordsStack.PushRange(valid);
						}
					}
					catch (Exception e)
					{
						state.Stop();
						exception = e;
					}
				});

			if (exception != null)
				throw exception;

			Word[] validWords = validWordsStack.Distinct(FreezableEqualityComparer<Word>.Default).ToArray();
			var matchList = new List<Word>();
			foreach (IGrouping<IEnumerable<Allomorph>, Word> group in validWords.GroupBy(validWord => validWord.AllomorphsInMorphOrder, MorphsEqualityComparer))
			{
				// enforce the disjunctive property of allomorphs by ensuring that this word synthesis
				// has the highest order of precedence for its allomorphs
				Word[] words = group.OrderBy(w => w.AllomorphsInMorphOrder, MorphsComparer).ToArray();
				int i;
				for (i = 0; i < words.Length; i++)
				{
					// if there is no free fluctuation with any allomorphs in the previous parse,
					// then the rest of the parses are invalid, because of disjunction
					if (i > 0 && words[i].AllomorphsInMorphOrder.Zip(words[i - 1].AllomorphsInMorphOrder).All(tuple => !tuple.Item1.FreeFluctuatesWith(tuple.Item2)))
						break;

					if (_lang.SurfaceStratum.SymbolTable.IsMatch(word, words[i].Shape))
					{
						_traceManager.ParseSuccessful(_lang, words[i]);
						matchList.Add(words[i]);
					}
					else
					{
						_traceManager.ParseFailed(_lang, words[i], FailureReason.SurfaceFormMismatch, null);
					}
				}

				for (; i < words.Length; i++)
					_traceManager.ParseFailed(_lang, words[i], FailureReason.DisjunctiveAllomorph, null);
			}

			return matchList;
		}

		internal IEnumerable<RootAllomorph> SearchRootAllomorphs(Stratum stratum, Shape shape)
		{
			RootAllomorphTrie alloSearcher = _allomorphTries[stratum];
			return alloSearcher.Search(shape).Distinct();
		}

		private IEnumerable<Word> LexicalLookup(Word input)
		{
			_traceManager.LexicalLookup(input.Stratum, input);
			foreach (LexEntry entry in SearchRootAllomorphs(input.Stratum, input.Shape).Select(allo => allo.Morpheme).Cast<LexEntry>().Where(LexEntrySelector).Distinct())
			{
				foreach (RootAllomorph allomorph in entry.Allomorphs)
				{
					Word newWord = input.DeepClone();
					newWord.RootAllomorph = allomorph;
					_traceManager.SynthesizeWord(_lang, newWord);
					newWord.Freeze();
					yield return newWord;
				}
			}
		}

		private bool IsWordValid(Word word)
		{
			if (!word.RealizationalFeatureStruct.IsUnifiable(word.SyntacticFeatureStruct) || word.CurrentMorphologicalRule != null)
			{
				_traceManager.ParseFailed(_lang, word, FailureReason.PartialParse, null);
				return false;
			}

			if (!word.ObligatorySyntacticFeatures.All(feature => ContainsFeature(word.SyntacticFeatureStruct, feature, new HashSet<FeatureStruct>(new ReferenceEqualityComparer<FeatureStruct>()))))
			{
				_traceManager.ParseFailed(_lang, word, FailureReason.ObligatorySyntacticFeatures, null);
				return false;
			}

			return word.Allomorphs.All(allo => allo.IsWordValid(this, word));
		}

		private bool ContainsFeature(FeatureStruct fs, Feature feature, ISet<FeatureStruct> visited)
		{
			if (visited.Contains(fs))
				return false;

			if (fs.ContainsFeature(feature))
				return true;

			if (fs.Features.OfType<ComplexFeature>().Any(cf => ContainsFeature(fs.GetValue(cf), feature, visited)))
				return true;

			return false;
		}
	}
}
