using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using Autofac;
using SayMore.Model;
using SayMore.Model.Fields;
using SayMore.Model.Files;
using SayMore.Model.Files.DataGathering;
using SayMore.Properties;
using SayMore.UI.ElementListScreen;
using SayMore.UI.Overview;
using SayMore.UI.ProjectWindow;

namespace SayMore
{
	/// ----------------------------------------------------------------------------------------
	/// <summary>
	/// TODO: it might be cleaner to remove this class and just have it all be in method
	/// on applicationContext
	/// </summary>
	/// ----------------------------------------------------------------------------------------
	public class ProjectContext : IDisposable
	{
		/// <summary>
		/// Any resources which belong only to this project will be tracked by this,
		/// and disposed of along with this ProjectContext class
		/// </summary>
		private ILifetimeScope _scope;

		public Project Project { get; private set; }
		public ProjectWindow ProjectWindow { get; private set; }

		private readonly AudioVideoDataGatherer _audioVideoDataGatherer;
		private readonly PresetGatherer _presetGatherer;
		private readonly AutoCompleteValueGatherer _autoCompleteValueGatherer;
		private readonly FieldGatherer _fieldGatherer;

		/// ------------------------------------------------------------------------------------
		public ProjectContext(string projectSettingsPath, IContainer parentContainer)
		{
			var rootDirectoryPath = Path.GetDirectoryName(projectSettingsPath);
			BuildSubContainerForThisProject(rootDirectoryPath, parentContainer);

			Project = _scope.Resolve<Func<string, Project>>()(projectSettingsPath);

			SetContributorsListToSession(Project.SessionsFolder);

			var peopleRepoFactory = _scope.Resolve<ElementRepository<Person>.Factory>();
			peopleRepoFactory(rootDirectoryPath, Person.kFolderName, _scope.Resolve<PersonFileType>());

			var sessionRepoFactory = _scope.Resolve<ElementRepository<Session>.Factory>();
			sessionRepoFactory(rootDirectoryPath, Session.kFolderName, _scope.Resolve<SessionFileType>());

			// Create background operations
			_presetGatherer = _scope.Resolve<PresetGatherer>();
			_autoCompleteValueGatherer = _scope.Resolve<AutoCompleteValueGatherer>();
			_audioVideoDataGatherer = _scope.Resolve<AudioVideoDataGatherer>();
			_fieldGatherer = _scope.Resolve<FieldGatherer>();

			// Start background operations
			_presetGatherer.Start();
			_autoCompleteValueGatherer.Start();
			_audioVideoDataGatherer.Start();
			_fieldGatherer.Start();

			var view1 = _scope.Resolve<ProjectScreen>();
			var view2 = _scope.Resolve<SessionsListScreen>();
			var view3 = _scope.Resolve<PersonListScreen>();

			var views = new ISayMoreView[]
			{
				view1,
				view2,
				view3
			};

			ProjectWindow = _scope.Resolve<ProjectWindow.Factory>()(projectSettingsPath, views);
		}

		///-------------------------------------------------------------------------------------------------------
		/// <summary>
		/// Set the contributor list to the session file from the metafiles
		/// </summary>
		/// <param name="sessionsFolder">Session folder path</param>
		///-------------------------------------------------------------------------------------------------------
		public static void SetContributorsListToSession(string sessionsFolder)
		{
			if (!Directory.Exists(sessionsFolder) || Path.GetFileName(sessionsFolder).ToLower() != "sessions")
			{
				return;
			}
			var dirLists = Directory.GetDirectories(sessionsFolder);
			foreach (var sessionFldrPath in dirLists)
			{
				var namesList = new SortedSet<string>();
				var nameRolesList = new SortedSet<string>();
				var contributorLists = new StringBuilder();
				var filesInDir = Directory.GetFiles(sessionFldrPath);
				var sessionFile = filesInDir.FirstOrDefault(f => f.EndsWith(".session"));
				if (sessionFile == null) return;
				var metaFilesList = filesInDir.Where(f => f.EndsWith(Settings.Default.MetadataFileExtension)).ToList();
				var sessionDoc = new XmlDocument();
				using (var sessionReader = XmlReader.Create(sessionFile))
				{
					sessionDoc.Load(sessionReader);
				}
				LoadContributors(sessionDoc, namesList, nameRolesList, contributorLists);
				var root = sessionDoc.DocumentElement;
				var contributionsNode = root?.SelectSingleNode(SessionFileType.kContributionsFieldName);
				contributionsNode?.ParentNode?.RemoveChild(contributionsNode); //Remove the contributions node
				if (root?.LastChild == null) continue;
				foreach (var metaFile in metaFilesList)
				{
					var metaFileDoc = new XmlDocument();
					using (var metaReader = XmlReader.Create(metaFile))
					{
						metaFileDoc.Load(metaReader);
					}
					LoadContributors(metaFileDoc, namesList, nameRolesList, contributorLists);
				}

				var participantsNode = root?.SelectSingleNode("participants") as XmlElement;
				if (participantsNode == null)
				{
					participantsNode = sessionDoc.CreateElement("participants");
					participantsNode.SetAttribute("type", "string");
					root.InsertAfter(participantsNode, root.LastChild);
				}
				participantsNode.InnerText = string.Join("; ", namesList);

				var newContributionsNode = sessionDoc.CreateElement(SessionFileType.kContributionsFieldName);
				newContributionsNode.SetAttribute("type", "xml");
				newContributionsNode.InnerXml = contributorLists.ToString();
				root.InsertAfter(newContributionsNode, root.LastChild);
				using (var sessionOutput = XmlWriter.Create(sessionFile, new XmlWriterSettings{Indent = true}))
				{
					sessionDoc.Save(sessionOutput);
				}
			}
		}

		private static void LoadContributors(XmlNode xmlDoc, SortedSet<string> namesList, SortedSet<string> nameRolesList, StringBuilder contributorLists)
		{

			var nodelist = xmlDoc.SelectNodes("//contributor");
			foreach (XmlNode node in nodelist)
			{
				var name = node["name"]?.InnerText;
				var role = node["role"]?.InnerText;
				var item = $@"{name} ({role})";
				if (nameRolesList.Contains(item)) continue;
				nameRolesList.Add(item);
				contributorLists.Append(node.OuterXml);
				namesList.Add(name); // Set will avoid duplicates.
			}
			// Check the participants list. Normally this legacy field is derived from contributions.
			// However, if this is a file from an older version of SayMore, it may not have contributions;
			// we need to migrate it. It might also have been edited outside SayMore and have
			// participants that are not known contributors, even though it has a contributions element.
			// So add in any participants we don't already have in some form.
			var particpantsNode = xmlDoc.SelectSingleNode("//participants");
			if (particpantsNode == null)
				return;
			foreach (var participant in particpantsNode.InnerText.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
			{
				var name = participant.Trim();

				// SayMore briefly had a state where roles were appended in parens to names in participants.
				// We don't want to create new contributors with names like Joe (consultant).
				// This fix could be unfortunate if someone wants to use a name like "Sally Smith (nee Jones)"
				// but we decided the danger of not handling the messed up data files was greater.
				var paren = name.IndexOf("(");
				if (paren >= 0)
					name = name.Substring(0, paren).Trim();
				// If we already have this person, with any role, we won't add again, since we don't have
				// any definite role information in the participants field
				if (name == "" || namesList.Contains(name))
					continue;

				// We have no way of knowing a role. But various code assumes it's not empty.
				// Since we created this contributor on the basis of finding a name in the participants list,
				// it seems a reasonable default to make the role 'participant'.
				// The specified date seems to be the one SayMore always uses. I don't even know what the
				// date of a contributor is supposed to mean.
				var item = $@"{name} (participant)";
				nameRolesList.Add(item);
				contributorLists.Append(
					$"<contributor><name>{name}</name><role>participant</role><date>0001-01-01</date><notes></notes></contributor>");
				namesList.Add(name); // Set will avoid duplicates.
			}
		}

		/// ------------------------------------------------------------------------------------
		protected void BuildSubContainerForThisProject(string rootDirectoryPath, IContainer parentContainer)
		{
			_scope = parentContainer.BeginLifetimeScope(builder =>
			{
				builder.RegisterType<ElementRepository<Session>>().InstancePerLifetimeScope();
				builder.RegisterType<ElementRepository<Person>>().InstancePerLifetimeScope();
				builder.RegisterType<ElementListViewModel<Session>>().InstancePerLifetimeScope();
				builder.RegisterType<ElementListViewModel<Person>>().InstancePerLifetimeScope();
				builder.RegisterType<AudioVideoDataGatherer>().InstancePerLifetimeScope();
				builder.RegisterType<IEnumerable<FileType>>().InstancePerLifetimeScope();

				builder.RegisterType<Project>().InstancePerLifetimeScope();

				builder.RegisterType<SessionFileType>().InstancePerLifetimeScope();
				builder.RegisterType<PersonFileType>().InstancePerLifetimeScope();
				builder.RegisterType<AnnotationFileType>().InstancePerLifetimeScope();
				builder.RegisterType<AnnotationFileWithMisingMediaFileType>().InstancePerLifetimeScope();
				builder.RegisterType<OralAnnotationFileType>().InstancePerLifetimeScope();

				//when something needs the list of filetypes, get them from this method
				builder.Register<IEnumerable<FileType>>(GetFilesTypes).InstancePerLifetimeScope();

				//these needed to be done later (as delegates) because of the FileTypes dependency
				//there's maybe something I'm doing wrong that requires me to register this twice like this...
				builder.Register<IProvideAudioVideoFileStatistics>(
					c => new AudioVideoDataGatherer(rootDirectoryPath,
						c.Resolve<IEnumerable<FileType>>())).InstancePerLifetimeScope();

				builder.Register<AudioVideoDataGatherer>(c => c.Resolve(typeof(IProvideAudioVideoFileStatistics))
						as AudioVideoDataGatherer).InstancePerLifetimeScope();

				//create a single PresetGatherer and stick it in the container
				//builder.RegisterInstance(parentContainer.Resolve<PresetGatherer.Factory>()(rootDirectoryPath));

				//using the factory gave stack overflow: builder.Register<PresetGatherer>(c => c.Resolve<PresetGatherer.Factory>()(rootDirectoryPath));
				builder.Register<PresetGatherer>(c => new PresetGatherer(rootDirectoryPath,
					GetDataGatheringFilesTypes(c), c.Resolve<PresetData.Factory>())).InstancePerLifetimeScope();

				builder.Register<AutoCompleteValueGatherer>(
					c => new AutoCompleteValueGatherer(rootDirectoryPath, GetDataGatheringFilesTypes(c),
						c.Resolve<Func<ProjectElement, string, ComponentFile>>())).InstancePerLifetimeScope();

				builder.Register<FieldGatherer>(
					c => new FieldGatherer(rootDirectoryPath, GetDataGatheringFilesTypes(c),
						c.Resolve<FileTypeFields.Factory>())).InstancePerLifetimeScope();

				builder.Register<FieldUpdater>(c => new FieldUpdater(c.Resolve<FieldGatherer>(),
					c.Resolve<IDictionary<string, IXmlFieldSerializer>>())).InstancePerLifetimeScope();

				builder.Register<ComponentFileFactory>(c => new ComponentFileFactory(
					c.Resolve<IEnumerable<FileType>>(),
					c.Resolve<IEnumerable<ComponentRole>>(),
					c.Resolve<XmlFileSerializer>(),
					c.Resolve<IProvideAudioVideoFileStatistics>(),
					c.Resolve<PresetGatherer>(),
					c.Resolve<FieldUpdater>()));

				// This replaces the ComponentFile.Factory that was expected in various constructors.
				builder.Register<Func<ProjectElement, string, ComponentFile>>(c => c.Resolve<ComponentFileFactory>().Create);

				//make a lazy factory-getter to get around a mysterious circular dependency problem
				//NB: when we move to .net 4, we can remove this and instead use Lazy<Func<PersonBasicEditor.Factory> in the PersonFileType constructor
				//builder.Register<Func<PersonBasicEditor.Factory>>(c => () => c.Resolve<PersonBasicEditor.Factory>());
				//builder.Register<Func<SessionBasicEditor.Factory>>(c => () => c.Resolve<SessionBasicEditor.Factory>());
			});
		}

		/// ------------------------------------------------------------------------------------
		private IEnumerable<FileType> GetFilesTypes(IComponentContext context)
		{
			return new List<FileType>(new FileType[]
			{
				context.Resolve<SessionFileType>(),
				context.Resolve<PersonFileType>(),
				context.Resolve<AnnotationFileType>(),
				context.Resolve<OralAnnotationFileType>(),	// This must come before AudioFileType.
				context.Resolve<AudioFileType>(),
				context.Resolve<VideoFileType>(),
				context.Resolve<ImageFileType>(),
				context.Resolve<AnnotationFileWithMisingMediaFileType>(),
				context.Resolve<UnknownFileType>(),
			});
		}

		/// ------------------------------------------------------------------------------------
		private IEnumerable<FileType> GetDataGatheringFilesTypes(IComponentContext context)
		{
			return new List<FileType>(new FileType[]
			{
				context.Resolve<SessionFileType>(),
				context.Resolve<PersonFileType>(),
				// REVIEW: Should this be a gathered type?
				// context.Resolve<OralAnnotationFileType>(),
				context.Resolve<AudioFileType>(),
				context.Resolve<VideoFileType>(),
				context.Resolve<ImageFileType>(),
				context.Resolve<UnknownFileType>(),
			});
		}

		/// ------------------------------------------------------------------------------------
		public void Dispose()
		{
			_audioVideoDataGatherer.Dispose();
			_presetGatherer.Dispose();
			_autoCompleteValueGatherer.Dispose();
			_fieldGatherer.Dispose();

			if (ProjectWindow != null)
			{
				ProjectWindow.Dispose();
				ProjectWindow = null;
			}

			_scope.Dispose();
			_scope = null;
			GC.Collect();
		}

		/// ------------------------------------------------------------------------------------
		public void SuspendAudioVideoBackgroundProcesses()
		{
			if (_audioVideoDataGatherer != null)
				_audioVideoDataGatherer.SuspendProcessing();
		}

		/// ------------------------------------------------------------------------------------
		public void ResumeAudioVideoBackgroundProcesses(bool processAllPendingEventsNow)
		{
			if (_audioVideoDataGatherer != null)
				_audioVideoDataGatherer.ResumeProcessing(processAllPendingEventsNow);
		}

		/// ------------------------------------------------------------------------------------
		public void SuspendBackgroundProcesses()
		{
			if (_audioVideoDataGatherer != null)
				_audioVideoDataGatherer.SuspendProcessing();

			if (_autoCompleteValueGatherer != null)
				_autoCompleteValueGatherer.SuspendProcessing();

			if (_fieldGatherer != null)
				_fieldGatherer.SuspendProcessing();

			if (_presetGatherer != null)
				_presetGatherer.SuspendProcessing();
		}

		/// ------------------------------------------------------------------------------------
		public void ResumeBackgroundProcesses(bool processAllPendingEventsNow)
		{
			if (_audioVideoDataGatherer != null)
				_audioVideoDataGatherer.ResumeProcessing(processAllPendingEventsNow);

			if (_autoCompleteValueGatherer != null)
				_autoCompleteValueGatherer.ResumeProcessing(processAllPendingEventsNow);

			if (_fieldGatherer != null)
				_fieldGatherer.ResumeProcessing(processAllPendingEventsNow);

			if (_presetGatherer != null)
				_presetGatherer.ResumeProcessing(processAllPendingEventsNow);
		}

		/// ------------------------------------------------------------------------------------
		public T ResolveForTests<T>() where T: class
		{
			return _scope.Resolve<T>();
		}
	}
}