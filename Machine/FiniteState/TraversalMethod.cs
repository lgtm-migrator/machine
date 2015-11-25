﻿using System;
using System.Collections.Generic;
using System.Linq;
using SIL.Collections;
using SIL.Machine.Annotations;
using SIL.Machine.FeatureModel;

namespace SIL.Machine.FiniteState
{
	internal abstract class TraversalMethod<TData, TOffset> where TData : IAnnotatedData<TOffset>
	{
		private readonly IEqualityComparer<NullableValue<TOffset>[,]> _registersEqualityComparer;
		private readonly Direction _dir;
		private readonly State<TData, TOffset> _startState;
		private readonly TData _data;
		private readonly bool _endAnchor;
		private readonly bool _unification;
		private readonly bool _useDefaults;
		private readonly Func<Annotation<TOffset>, bool> _filter;
		private readonly List<Annotation<TOffset>> _annotations; 

		protected TraversalMethod(IEqualityComparer<NullableValue<TOffset>[,]> registersEqualityComparer, Direction dir, Func<Annotation<TOffset>, bool> filter, State<TData, TOffset> startState,
			TData data, bool endAnchor, bool unification, bool useDefaults)
		{
			_registersEqualityComparer = registersEqualityComparer;
			_dir = dir;
			_filter = filter;
			_startState = startState;
			_data = data;
			_endAnchor = endAnchor;
			_unification = unification;
			_useDefaults = useDefaults;
			IEnumerable<Annotation<TOffset>> anns = _data.Annotations.GetNodes(_dir).SelectMany(a => a.GetNodesDepthFirst(_dir)).Where(a => _filter(a));
			switch (_dir)
			{
				case Direction.LeftToRight:
					anns = anns.OrderBy(a => a.Span).ThenBy(a => a.Depth);
					break;
				case Direction.RightToLeft:
					anns = anns.OrderByDescending(a => a.Span).ThenBy(a => a.Depth);
					break;
			}
			_annotations = anns.ToList();
		}

		protected TData Data
		{
			get { return _data; }
		}

		protected IEqualityComparer<NullableValue<TOffset>[,]> RegistersEqualityComparer
		{
			get { return _registersEqualityComparer; }
		}

		public IList<Annotation<TOffset>> Annotations
		{
			get { return _annotations; }
		}

		public abstract IEnumerable<FstResult<TData, TOffset>> Traverse(ref int annIndex, NullableValue<TOffset>[,] initRegisters, IList<TagMapCommand> initCmds, ISet<int> initAnns);

		protected static void ExecuteCommands(NullableValue<TOffset>[,] registers, IEnumerable<TagMapCommand> cmds,
			NullableValue<TOffset> start, NullableValue<TOffset> end)
		{
			foreach (TagMapCommand cmd in cmds)
			{
				if (cmd.Src == TagMapCommand.CurrentPosition)
				{
					registers[cmd.Dest, 0] = start;
					registers[cmd.Dest, 1] = end;
				}
				else
				{
					registers[cmd.Dest, 0] = registers[cmd.Src, 0];
					registers[cmd.Dest, 1] = registers[cmd.Src, 1];
				}
			}
		}

		protected bool CheckInputMatch(Arc<TData, TOffset> arc, int annIndex, VariableBindings varBindings)
		{
			return annIndex < _annotations.Count && arc.Input.Matches(_annotations[annIndex].FeatureStruct, _unification, _useDefaults, varBindings);
		}

		private void CheckAccepting(int annIndex, NullableValue<TOffset>[,] registers, TData output,
			VariableBindings varBindings, Arc<TData, TOffset> arc, ICollection<FstResult<TData, TOffset>> curResults, int[] priorities)
		{
			if (arc.Target.IsAccepting && (!_endAnchor || annIndex == _annotations.Count))
			{
				Annotation<TOffset> ann = annIndex < _annotations.Count ? _annotations[annIndex] : _data.Annotations.GetEnd(_dir);
				var matchRegisters = (NullableValue<TOffset>[,]) registers.Clone();
				ExecuteCommands(matchRegisters, arc.Target.Finishers, new NullableValue<TOffset>(), new NullableValue<TOffset>());
				if (arc.Target.AcceptInfos.Count > 0)
				{
					foreach (AcceptInfo<TData, TOffset> acceptInfo in arc.Target.AcceptInfos)
					{
						TData resOutput = output;
						var cloneable = resOutput as IDeepCloneable<TData>;
						if (cloneable != null)
							resOutput = cloneable.DeepClone();

						var candidate = new FstResult<TData, TOffset>(_registersEqualityComparer, acceptInfo.ID, matchRegisters, resOutput, varBindings.DeepClone(),
							acceptInfo.Priority, arc.Target.IsLazy, ann, priorities, curResults.Count);
						if (acceptInfo.Acceptable == null || acceptInfo.Acceptable(_data, candidate))
							curResults.Add(candidate);
					}
				}
				else
				{
					TData resOutput = output;
					var cloneable = resOutput as IDeepCloneable<TData>;
					if (cloneable != null)
						resOutput = cloneable.DeepClone();
					curResults.Add(new FstResult<TData, TOffset>(_registersEqualityComparer, null, matchRegisters, resOutput, varBindings.DeepClone(), -1, arc.Target.IsLazy, ann,
						priorities, curResults.Count));
				}
			}
		}

		protected IEnumerable<TInst> Initialize<TInst>(ref int annIndex, NullableValue<TOffset>[,] registers,
			IList<TagMapCommand> cmds, ISet<int> initAnns, Func<State<TData, TOffset>, int, NullableValue<TOffset>[,], VariableBindings, TInst> instFactory)
		{
			var insts = new List<TInst>();
			TOffset offset = _annotations[annIndex].Span.GetStart(_dir);

			var newRegisters = (NullableValue<TOffset>[,]) registers.Clone();
			ExecuteCommands(newRegisters, cmds, new NullableValue<TOffset>(offset), new NullableValue<TOffset>());

			for (int i = annIndex; i < _annotations.Count && _annotations[i].Span.GetStart(_dir).Equals(offset); i++)
			{
				if (_annotations[i].Optional)
				{
					int nextIndex = GetNextNonoverlappingAnnotationIndex(i);
					if (nextIndex != -1)
						insts.AddRange(Initialize(ref nextIndex, registers, cmds, initAnns, instFactory));
				}
			}

			bool cloneRegisters = false;
			for (; annIndex < _annotations.Count && _annotations[annIndex].Span.GetStart(_dir).Equals(offset); annIndex++)
			{
				if (!initAnns.Contains(annIndex))
				{
					insts.Add(instFactory(_startState, annIndex, cloneRegisters ? (NullableValue<TOffset>[,]) newRegisters.Clone() : newRegisters, new VariableBindings()));
					initAnns.Add(annIndex);
					cloneRegisters = true;
				}
			}

			return insts;
		}

		protected IEnumerable<TInst> Advance<TInst>(int annIndex, NullableValue<TOffset>[,] registers, TData output, VariableBindings varBindings,
			Arc<TData, TOffset> arc, ICollection<FstResult<TData, TOffset>> curResults, int[] priorities,
			Func<State<TData, TOffset>, int, NullableValue<TOffset>[,], VariableBindings, bool, TInst> instFactory)
		{
			int nextIndex = GetNextNonoverlappingAnnotationIndex(annIndex);
			TOffset nextOffset = nextIndex < _annotations.Count ? _annotations[nextIndex].Span.GetStart(_dir) : _data.Annotations.GetLast(_dir, _filter).Span.GetEnd(_dir);
			TOffset end = _annotations[annIndex].Span.GetEnd(_dir);
			var newRegisters = (NullableValue<TOffset>[,]) registers.Clone();
			ExecuteCommands(newRegisters, arc.Commands, new NullableValue<TOffset>(nextOffset), new NullableValue<TOffset>(end));

			CheckAccepting(nextIndex, newRegisters, output, varBindings, arc, curResults, priorities);

			if (nextIndex < _annotations.Count)
			{
				var anns = new List<int>();
				bool cloneOutputs = false;
				for (int i = nextIndex; i < _annotations.Count && _annotations[i].Span.GetStart(_dir).Equals(nextOffset); i++)
				{
					if (_annotations[i].Optional)
					{
						foreach (TInst ni in Advance(i, registers, output, varBindings, arc, curResults, priorities, instFactory))
						{
							yield return ni;
							cloneOutputs = true;
						}
					}
					anns.Add(i);
				}

				bool cloneRegisters = false;
				foreach (int curIndex in anns)
				{
					yield return instFactory(arc.Target, curIndex, cloneRegisters ? (NullableValue<TOffset>[,]) newRegisters.Clone() : newRegisters,
						cloneOutputs ? varBindings.DeepClone() : varBindings, cloneOutputs);
					cloneOutputs = true;
					cloneRegisters = true;
				}
			}
			else
			{
				yield return instFactory(arc.Target, nextIndex, newRegisters, varBindings, false);
			}
		}

		protected TInst EpsilonAdvance<TInst>(int annIndex, NullableValue<TOffset>[,] registers, TData output, VariableBindings varBindings, Arc<TData, TOffset> arc,
			ICollection<FstResult<TData, TOffset>> curResults, int[] priorities,
			Func<State<TData, TOffset>, int, NullableValue<TOffset>[,], VariableBindings, TInst> instFactory)
		{
			Annotation<TOffset> ann = annIndex < _annotations.Count ? _annotations[annIndex] : _data.Annotations.GetEnd(_dir);
			int prevIndex = GetPrevNonoverlappingAnnotationIndex(annIndex);
			Annotation<TOffset> prevAnn = _annotations[prevIndex];
			ExecuteCommands(registers, arc.Commands, new NullableValue<TOffset>(ann.Span.GetStart(_dir)), new NullableValue<TOffset>(prevAnn.Span.GetEnd(_dir)));
			CheckAccepting(annIndex, registers, output, varBindings, arc, curResults, priorities);
			return instFactory(arc.Target, annIndex, registers, varBindings);
		}

		private int GetNextNonoverlappingAnnotationIndex(int start)
		{
			Annotation<TOffset> cur = _annotations[start];
			for (int i = start + 1; i < _annotations.Count; i++)
			{
				if (!cur.Span.Overlaps(_annotations[i].Span))
					return i;
			}
			return _annotations.Count;
		}

		private int GetPrevNonoverlappingAnnotationIndex(int start)
		{
			Annotation<TOffset> cur = start < _annotations.Count ? _annotations[start] : _data.Annotations.GetEnd(_dir);
			for (int i = start - 1; i >= 0; i--)
			{
				if (!cur.Span.Overlaps(_annotations[i].Span))
					return i;
			}
			return -1;
		}

		protected static int[] UpdatePriorities(int[] priorities, int priority)
		{
			var p = new int[priorities.Length + 1];
			priorities.CopyTo(p, 0);
			p[p.Length - 1] = priority;
			return p;
		}

		protected class Instance
		{
			private readonly NullableValue<TOffset>[,] _registers;
			private readonly VariableBindings _varBindings;
			private readonly State<TData, TOffset> _state;
			private readonly int _annotationIndex;

			public Instance(State<TData, TOffset> state, int annotationIndex, NullableValue<TOffset>[,] registers, VariableBindings varBindings)
			{
				_state = state;
				_annotationIndex = annotationIndex;
				_registers = registers;
				_varBindings = varBindings;
			}

			public State<TData, TOffset> State
			{
				get { return _state; }
			}

			public int AnnotationIndex
			{
				get { return _annotationIndex; }
			}

			public NullableValue<TOffset>[,] Registers
			{
				get { return _registers; }
			}

			public VariableBindings VariableBindings
			{
				get { return _varBindings; }
			}
		}
	}
}
