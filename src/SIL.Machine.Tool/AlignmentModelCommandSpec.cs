﻿using System.Collections.Generic;
using System.IO;
using System.Linq;
using McMaster.Extensions.CommandLineUtils;
using SIL.Machine.Corpora;
using SIL.Machine.Plugin;
using SIL.Machine.Translation;
using SIL.Machine.Translation.Thot;

namespace SIL.Machine
{
	public class AlignmentModelCommandSpec : ICommandSpec
	{
		private const string Och = "och";
		private const string Union = "union";
		private const string Intersection = "intersection";
		private const string Grow = "grow";
		private const string GrowDiag = "grow-diag";
		private const string GrowDiagFinal = "grow-diag-final";
		private const string GrowDiagFinalAnd = "grow-diag-final-and";

		private CommandArgument _modelArgument;
		private CommandOption _modelTypeOption;
		private CommandOption _smtModelTypeOption;
		private CommandOption _pluginOption;
		private CommandOption _symHeuristicOption;

		private IWordAlignmentModelFactory _modelFactory;

		public bool IncludeSymHeuristicOption { get; set; } = true;

		public void AddParameters(CommandBase command)
		{
			_modelArgument = command.Argument("MODEL_PATH", "The word alignment model.").IsRequired();
			_modelTypeOption = command.Option("-mt|--model-type <MODEL_TYPE>",
				$"The word alignment model type.\nTypes: \"{ToolHelpers.Hmm}\" (default), \"{ToolHelpers.Ibm1}\", \"{ToolHelpers.Ibm2}\", \"{ToolHelpers.FastAlign}\", \"{ToolHelpers.Smt}\".",
				CommandOptionType.SingleValue);
			_smtModelTypeOption = command.Option("-smt|--smt-model-type <SMT_MODEL_TYPE>",
				$"The SMT model type.\nTypes: \"{ToolHelpers.Hmm}\" (default), \"{ToolHelpers.Ibm1}\", \"{ToolHelpers.Ibm2}\", \"{ToolHelpers.FastAlign}\".",
				CommandOptionType.SingleValue);
			_pluginOption = command.Option("-mp|--model-plugin <PLUGIN_FILE>", "The model plugin file.",
				CommandOptionType.SingleValue);
			if (IncludeSymHeuristicOption)
			{
				_symHeuristicOption = command.Option("-sh|--sym-heuristic <SYM_HEURISTIC>",
					$"The symmetrization heuristic.\nHeuristics: \"{Och}\" (default), \"{Union}\", \"{Intersection}\", \"{Grow}\", \"{GrowDiag}\", \"{GrowDiagFinal}\", \"{GrowDiagFinalAnd}\".",
					CommandOptionType.SingleValue);
			}
		}

		public bool Validate(TextWriter outWriter)
		{
			if (_pluginOption.HasValue() && _pluginOption.Values.Any(p => !File.Exists(p)))
			{
				outWriter.WriteLine("A specified plugin file does not exist.");
				return false;
			}

			var pluginLoader = new PluginManager(_pluginOption.Values);
			var factories = pluginLoader.Create<IWordAlignmentModelFactory>().ToDictionary(f => f.ModelType);

			if (!ValidateAlignmentModelTypeOption(_modelTypeOption.Value(), factories.Keys))
			{
				outWriter.WriteLine("The specified word alignment model type is invalid.");
				return false;
			}

			if (!ToolHelpers.ValidateTranslationModelTypeOption(_smtModelTypeOption.Value()))
			{
				outWriter.WriteLine("The specified SMT model type is invalid.");
				return false;
			}

			if (!ValidateSymmetrizationHeuristicOption(_symHeuristicOption?.Value()))
			{
				outWriter.WriteLine("The specified symmetrization heuristic is invalid.");
				return false;
			}

			if (factories.TryGetValue(_modelTypeOption.Value(), out IWordAlignmentModelFactory factory))
				_modelFactory = factory;

			return true;
		}

		public IWordAlignmentModel CreateAlignmentModel()
		{
			if (_modelFactory != null)
				return _modelFactory.CreateModel(_modelArgument.Value);

			switch (_modelTypeOption.Value())
			{
				default:
				case ToolHelpers.Hmm:
					return CreateThotAlignmentModel<HmmWordAlignmentModel>();
				case ToolHelpers.Ibm1:
					return CreateThotAlignmentModel<Ibm1WordAlignmentModel>();
				case ToolHelpers.Ibm2:
					return CreateThotAlignmentModel<Ibm2WordAlignmentModel>();
				case ToolHelpers.FastAlign:
					return CreateThotAlignmentModel<FastAlignWordAlignmentModel>();
				case ToolHelpers.Smt:
					string modelCfgFileName = ToolHelpers.GetTranslationModelConfigFileName(_modelArgument.Value);
					switch (_smtModelTypeOption.Value())
					{
						default:
						case ToolHelpers.Hmm:
							return CreateThotSmtAlignmentModel<HmmWordAlignmentModel>(modelCfgFileName);
						case ToolHelpers.Ibm1:
							return CreateThotSmtAlignmentModel<Ibm1WordAlignmentModel>(modelCfgFileName);
						case ToolHelpers.Ibm2:
							return CreateThotSmtAlignmentModel<Ibm2WordAlignmentModel>(modelCfgFileName);
						case ToolHelpers.FastAlign:
							return CreateThotSmtAlignmentModel<FastAlignWordAlignmentModel>(modelCfgFileName);
					}
			}
		}

		public bool IsSegmentInvalid(ParallelTextSegment segment)
		{
			return segment.IsEmpty || (_modelTypeOption.Value() == ToolHelpers.Smt
				&& segment.SourceSegment.Count > TranslationConstants.MaxSegmentLength);
		}

		public ITrainer CreateAlignmentModelTrainer(ParallelTextCorpus corpus, int maxSize)
		{
			if (_modelFactory != null)
			{
				ITokenProcessor processor = TokenProcessors.Pipeline(TokenProcessors.Normalize,
					TokenProcessors.Lowercase);
				return _modelFactory.CreateTrainer(_modelArgument.Value, processor, processor, corpus, maxSize);
			}

			switch (_modelTypeOption.Value())
			{
				default:
				case ToolHelpers.Hmm:
					return CreateThotAlignmentModelTrainer<HmmWordAlignmentModel>(corpus, maxSize);
				case ToolHelpers.Ibm1:
					return CreateThotAlignmentModelTrainer<Ibm1WordAlignmentModel>(corpus, maxSize);
				case ToolHelpers.Ibm2:
					return CreateThotAlignmentModelTrainer<Ibm2WordAlignmentModel>(corpus, maxSize);
				case ToolHelpers.FastAlign:
					return CreateThotAlignmentModelTrainer<FastAlignWordAlignmentModel>(corpus, maxSize);
				case ToolHelpers.Smt:
					string modelCfgFileName = ToolHelpers.GetTranslationModelConfigFileName(_modelArgument.Value);
					return ToolHelpers.CreateTranslationModelTrainer(_smtModelTypeOption.Value(), modelCfgFileName,
						corpus, maxSize);
			}
		}

		private ITrainer CreateThotAlignmentModelTrainer<TAlignModel>(ParallelTextCorpus corpus, int maxSize)
			where TAlignModel : ThotWordAlignmentModelBase<TAlignModel>, new()
		{
			string modelPath = _modelArgument.Value;
			if (ToolHelpers.IsDirectoryPath(modelPath))
				modelPath = Path.Combine(modelPath, "src_trg");
			string modelDir = Path.GetDirectoryName(modelPath);
			if (!Directory.Exists(modelDir))
				Directory.CreateDirectory(modelDir);
			ITokenProcessor processor = TokenProcessors.Pipeline(TokenProcessors.Normalize, TokenProcessors.Lowercase);
			var directTrainer = new ThotWordAlignmentModelTrainer<TAlignModel>(modelPath + "_invswm", processor,
				processor, corpus, maxSize);
			var inverseTrainer = new ThotWordAlignmentModelTrainer<TAlignModel>(modelPath + "_swm", processor,
				processor, corpus.Invert(), maxSize);
			return new SymmetrizedWordAlignmentModelTrainer(directTrainer, inverseTrainer);
		}

		private IWordAlignmentModel CreateThotAlignmentModel<TAlignModel>()
			where TAlignModel : ThotWordAlignmentModelBase<TAlignModel>, new()
		{
			string modelPath = _modelArgument.Value;
			if (ToolHelpers.IsDirectoryPath(modelPath))
				modelPath = Path.Combine(modelPath, "src_trg");

			var directModel = new TAlignModel();
			directModel.Load(modelPath + "_invswm");

			var inverseModel = new TAlignModel();
			inverseModel.Load(modelPath + "_swm");

			SymmetrizationHeuristic heuristic;
			switch (_symHeuristicOption?.Value())
			{
				default:
				case Och:
					heuristic = SymmetrizationHeuristic.Och;
					break;
				case Union:
					heuristic = SymmetrizationHeuristic.Union;
					break;
				case Intersection:
					heuristic = SymmetrizationHeuristic.Intersection;
					break;
				case Grow:
					heuristic = SymmetrizationHeuristic.Grow;
					break;
				case GrowDiag:
					heuristic = SymmetrizationHeuristic.GrowDiag;
					break;
				case GrowDiagFinal:
					heuristic = SymmetrizationHeuristic.GrowDiagFinal;
					break;
				case GrowDiagFinalAnd:
					heuristic = SymmetrizationHeuristic.GrowDiagFinalAnd;
					break;
			}

			return new SymmetrizedWordAlignmentModel(directModel, inverseModel) { Heuristic = heuristic };
		}

		private IWordAlignmentModel CreateThotSmtAlignmentModel<TAlignModel>(string modelCfgFileName)
			where TAlignModel : ThotWordAlignmentModelBase<TAlignModel>, new()
		{
			return new ThotSmtWordAlignmentModel<TAlignModel>(modelCfgFileName);
		}

		private static bool ValidateAlignmentModelTypeOption(string value, IEnumerable<string> pluginTypes)
		{
			var validTypes = new HashSet<string>
			{
				ToolHelpers.Hmm,
				ToolHelpers.Ibm1,
				ToolHelpers.Ibm2,
				ToolHelpers.FastAlign,
				ToolHelpers.Smt
			};
			validTypes.UnionWith(pluginTypes);
			return string.IsNullOrEmpty(value) || validTypes.Contains(value);
		}

		private static bool ValidateSymmetrizationHeuristicOption(string value)
		{
			var validHeuristics = new HashSet<string>
			{
				Och,
				Union,
				Intersection,
				Grow,
				GrowDiag,
				GrowDiagFinal,
				GrowDiagFinalAnd
			};
			return string.IsNullOrEmpty(value) || validHeuristics.Contains(value);
		}
	}
}