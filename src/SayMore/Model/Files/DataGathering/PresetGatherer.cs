using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace SayMore.Model.Files.DataGathering
{
	/// <summary>
	/// Gets all the metadata settings found in the whole project,
	/// for the purpose of automatically making presets
	/// </summary>
	public class PresetGatherer : BackgroundFileProcessor<PresetData>
	{
		public delegate PresetGatherer Factory(string rootDirectoryPath);

		public PresetGatherer(string rootDirectoryPath, IEnumerable<FileType> allFileTypes,  PresetData.Factory presetFactory)
			:	base(rootDirectoryPath,
							from t in allFileTypes where t.IsAudioOrVideo select t,
				path=>presetFactory(path))
		{
		}

		public IEnumerable<KeyValuePair<string, Dictionary<string, string>>> GetSuggestions()
		{
			var suggestor = new UniqueCombinationsFinder(
				from d in _fileToDataDictionary.Values
				select d.Dictionary);
			return suggestor.GetSuggestions();
		}
	}

	/// <summary>
	/// The preset which would be derived from this file
	/// </summary>
	public class PresetData
	{
		public delegate PresetData FactoryForTest(string path, Func<string, Dictionary<string, string>> pathToDictionaryFunction);
		public delegate PresetData Factory(string path);
		public Dictionary<string, string> Dictionary { get; private set; }

		/// <summary>
		/// Notice, it's up to the caller to give us files which make sense.
		/// E.g., media files have sidecars with data that makes sense as a presets.
		/// </summary>
		public PresetData(string path, ComponentFile.Factory componentFileFactory)
		{
			var f = componentFileFactory(path);
			Dictionary =  f.MetaDataFieldValues.ToDictionary(field => field.FieldDefinitionKey,
															field => field.Value);
		}

		/// <summary>
		/// for test only... probably was a waste of time
		/// </summary>
		public PresetData(string path, Func<string, Dictionary<string, string>> pathToDictionaryFunction)
		{
			Dictionary = pathToDictionaryFunction(path);
		}
	}
}
