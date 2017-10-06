﻿using System.Collections.Generic;
using System.Linq;
using SIL.ObjectModel;
using System;

namespace SIL.Machine.Translation.Thot
{
	internal class ThotInteractiveTranslationSession : DisposableBase, IInteractiveTranslationSession
	{
		private readonly ThotSmtEngine _engine;
		private readonly IReadOnlyList<string> _sourceSegment; 
		private List<string> _prefix;
		private bool _isLastWordComplete;
		private TranslationResult _currentResult;
		private readonly ErrorCorrectionWordGraphProcessor _wordGraphProcessor;

		public ThotInteractiveTranslationSession(ThotSmtEngine engine, IReadOnlyList<string> sourceSegment,
			WordGraph wordGraph)
		{
			_engine = engine;
			_sourceSegment = sourceSegment;
			_prefix = new List<string>();
			_isLastWordComplete = true;
			_wordGraphProcessor = new ErrorCorrectionWordGraphProcessor(_engine.ErrorCorrectionModel, wordGraph);
			_currentResult = CreateInteractiveResult();
		}

		public IReadOnlyList<string> SourceSegment
		{
			get
			{
				CheckDisposed();
				return _sourceSegment;
			}
		}

		public IReadOnlyList<string> Prefix
		{
			get
			{
				CheckDisposed();
				return _prefix;
			}
		}

		public bool IsLastWordComplete
		{
			get
			{
				CheckDisposed();
				return _isLastWordComplete;
			}
		}

		public TranslationResult CurrentResult
		{
			get
			{
				CheckDisposed();
				return _currentResult;
			}
		}

		private TranslationResult CreateInteractiveResult()
		{
			TranslationInfo correction = _wordGraphProcessor.Correct(_prefix.ToArray(), _isLastWordComplete, 1)
				.FirstOrDefault();
			return _engine.CreateResult(_sourceSegment, _prefix.Count, correction);
		}

		public TranslationResult SetPrefix(IReadOnlyList<string> prefix, bool isLastWordComplete)
		{
			CheckDisposed();

			if (!_prefix.SequenceEqual(prefix) || _isLastWordComplete != isLastWordComplete)
			{
				_prefix.Clear();
				_prefix.AddRange(prefix);
				_isLastWordComplete = isLastWordComplete;
				_currentResult = CreateInteractiveResult();
			}
			return _currentResult;
		}

		public TranslationResult AppendToPrefix(string addition, bool isLastWordComplete)
		{
			CheckDisposed();

			if (string.IsNullOrEmpty(addition) && _isLastWordComplete)
			{
				throw new ArgumentException(
					"An empty string cannot be added to a prefix where the last word is complete.", nameof(addition));
			}

			if (!string.IsNullOrEmpty(addition) || isLastWordComplete != _isLastWordComplete)
			{
				if (_isLastWordComplete)
					_prefix.Add(addition);
				else
					_prefix[_prefix.Count - 1] = _prefix[_prefix.Count - 1] + addition;
				_isLastWordComplete = isLastWordComplete;
				_currentResult = CreateInteractiveResult();
			}
			return _currentResult;
		}

		public TranslationResult AppendToPrefix(IEnumerable<string> words)
		{
			CheckDisposed();

			bool updated = false;
			foreach (string word in words)
			{
				if (_isLastWordComplete)
					_prefix.Add(word);
				else
					_prefix[_prefix.Count - 1] = word;
				_isLastWordComplete = true;
				updated = true;
			}
			if (updated)
				_currentResult = CreateInteractiveResult();
			return _currentResult;
		}

		public void Approve()
		{
			CheckDisposed();

			_engine.TrainSegment(_sourceSegment, _prefix);
		}

		protected override void DisposeManagedResources()
		{
			_engine.RemoveSession(this);
		}
	}
}
