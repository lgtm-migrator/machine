using SIL.Collections;
using SIL.Machine.FeatureModel;
using SIL.Machine.FiniteState;

namespace SIL.Machine.Matching
{
    /// <summary>
    /// This class represents a simple context in a phonetic pattern. Simple contexts are used to represent
    /// natural classes and segments in a pattern.
    /// </summary>
	public class Constraint<TData, TOffset> : PatternNode<TData, TOffset>, IValueEquatable<Constraint<TData, TOffset>>, IDeepCloneable<Constraint<TData, TOffset>> where TData : IData<TOffset>, IDeepCloneable<TData>
    {
    	private readonly FeatureStruct _fs;

        /// <summary>
		/// Initializes a new instance of the <see cref="Constraint{TData, TOffset}"/> class.
        /// </summary>
		public Constraint(FeatureStruct fs)
		{
			_fs = fs;
		}

    	/// <summary>
    	/// Copy constructor.
    	/// </summary>
    	/// <param name="constraint">The annotation constraints.</param>
		protected Constraint(Constraint<TData, TOffset> constraint)
        {
            _fs = constraint._fs.DeepClone();
        }

        /// <summary>
        /// Gets the feature values.
        /// </summary>
        /// <value>The feature values.</value>
        public FeatureStruct FeatureStruct
        {
            get { return _fs; }
        }

		protected override bool CanAdd(PatternNode<TData, TOffset> child)
		{
			return false;
		}

		internal override State<TData, TOffset> GenerateNfa(Fst<TData, TOffset> fsa, State<TData, TOffset> startState, out bool hasVariables)
		{
			hasVariables = _fs.HasVariables;
			FeatureStruct condition = _fs;
			if (!_fs.IsFrozen)
			{
				condition = _fs.DeepClone();
				condition.Freeze();
			}
			return startState.Arcs.Add(condition, fsa.CreateState());
		}

		protected override int FreezeImpl()
		{
			int code = base.FreezeImpl();
			_fs.Freeze();
			code = code * 31 + _fs.GetValueHashCode();
			return code;
		}

		protected override PatternNode<TData, TOffset> DeepCloneImpl()
		{
			return DeepClone();
		}

    	public new Constraint<TData, TOffset> DeepClone()
    	{
			return new Constraint<TData, TOffset>(this);
    	}

		public override bool ValueEquals(PatternNode<TData, TOffset> other)
		{
			var otherCons = other as Constraint<TData, TOffset>;
			return otherCons != null && ValueEquals(otherCons);
		}

		public bool ValueEquals(Constraint<TData, TOffset> other)
		{
			return _fs.ValueEquals(other._fs);
		}

    	public override string ToString()
		{
			return _fs.ToString();
		}
    }
}
