using SIL.Machine.FeatureModel;
using SIL.Machine.Fsa;

namespace SIL.Machine.Matching
{
    /// <summary>
    /// This class represents a simple context in a phonetic pattern. Simple contexts are used to represent
    /// natural classes and segments in a pattern.
    /// </summary>
	public class Constraint<TData, TOffset> : PatternNode<TData, TOffset> where TData : IData<TOffset>
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
		public Constraint(Constraint<TData, TOffset> constraint)
        {
            _fs = constraint._fs.Clone();
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

		internal override State<TData, TOffset> GenerateNfa(FiniteStateAutomaton<TData, TOffset> fsa, State<TData, TOffset> startState)
		{
    		return startState.AddArc(_fs.Clone(), fsa.CreateState());
		}

		public override PatternNode<TData, TOffset> Clone()
		{
			return new Constraint<TData, TOffset>(this);
		}

		public override string ToString()
		{
			return string.Format("[{0}]", _fs);
		}
    }
}
