using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualBasic.FileIO;
using Palaso.Code;
using SayMore.Model.Files;
using SayMore.Properties;

namespace SayMore.Model
{
	/// ----------------------------------------------------------------------------------------
	/// <summary>
	/// This is reposible for finding, creating, and removing items of the given type T
	/// (i.e. Events or People)
	/// </summary>
	/// ----------------------------------------------------------------------------------------
	public class ElementRepository<T> where T : ProjectElement
	{
		public delegate void ElementIdChangedHandler(ProjectElement element, string oldId, string newId);
		public event ElementIdChangedHandler ElementIdChanged;

		public delegate ElementRepository<T> Factory(string projectDirectory, string elementGroupName, FileType type);

		public delegate T ElementFactory<T>(string parentElementFolder, string id) where T : ProjectElement;

		//private readonly  Func<string, string, T> _elementFactory;
		private readonly ElementFactory<T> _elementFactory; //TODO: fix this. I'm struggling with autofac on this issue
		private readonly List<T> _items = new List<T>();
		private readonly string _rootFolder;

		/// ------------------------------------------------------------------------------------
		public ElementRepository(string projectDirectory, string elementGroupName,
			FileType type, ElementFactory<T> elementFactory)
		{
			ElementFileType = type;
			_elementFactory = elementFactory;
			RequireThat.Directory(projectDirectory).Exists();

			_rootFolder = Path.Combine(projectDirectory, elementGroupName);

			if (!Directory.Exists(_rootFolder))
				Directory.CreateDirectory(_rootFolder);

			RefreshItemList();
		}

		[Obsolete("For Mocking Only")]
		public ElementRepository(){}

		/// ------------------------------------------------------------------------------------
		/// <summary>
		/// Updates the list of items by looking in the file system for all the subfolders
		/// in the project's folder corresponding to this repository.
		/// </summary>
		/// ------------------------------------------------------------------------------------
		public void RefreshItemList()
		{
			var folders = new HashSet<string>(Directory.GetDirectories(_rootFolder));

			// Go through the existing events we have and remove
			// any that no longer have a events folder.
			for (int i = _items.Count() - 1; i >= 0; i--)
			{
				if (!folders.Contains(_items[i].FolderPath))
					_items.RemoveAt(i);
			}

			// Add any items we don't already have
			foreach (string path in folders)
			{
				var item = _items.FirstOrDefault(x => x.FolderPath == path);
				if (item == null)
				{
					var element =_elementFactory(Path.GetDirectoryName(path),
						Path.GetFileNameWithoutExtension(path));

					element.IdChangedAction = OnElementIdChanged;
					_items.Add(element);
				}
			}
		}

		/// ------------------------------------------------------------------------------------
		public IEnumerable<T> AllItems
		{
			get {return _items;}
		}

		/// ------------------------------------------------------------------------------------
		public string PathToFolder
		{
			get { return _rootFolder; }
		}

		/// ------------------------------------------------------------------------------------
		public FileType ElementFileType { get; private set; }

		/// ------------------------------------------------------------------------------------
		public T CreateNew(string id)
		{
			T element = _elementFactory(_rootFolder, id);
			element.IdChangedAction = OnElementIdChanged;
			_items.Add(element);
			return element;
		}

		/// ------------------------------------------------------------------------------------
		public bool Remove(string id)
		{
			var item = _items.FirstOrDefault(x => x.Id == id);
			return (item == null ? false : Remove(item));
		}

		/// ------------------------------------------------------------------------------------
		public bool Remove(T item)
		{
			if (!_items.Contains(item))
				return false;

			try
			{
#if !MONO
				FileSystem.DeleteDirectory(item.FolderPath,
					(Settings.Default.PreventDeleteElementSystemConfirmationMessage ?
					UIOption.OnlyErrorDialogs : UIOption.AllDialogs), RecycleOption.SendToRecycleBin);
#else
				// TODO: Find a way in Mono to send something to the recycle bin.
				Directory.Delete(item.FolderPath);
#endif
			}
			catch (Exception e)
			{
				Palaso.Reporting.ErrorReport.ReportNonFatalException(e);
				return false;
			}

			_items.Remove(item);
			return true;
		}

		/// ------------------------------------------------------------------------------------
		public virtual T GetById(string id)
		{
			return _items.FirstOrDefault(x => x.Id == id);
		}

		/// ------------------------------------------------------------------------------------
		protected void OnElementIdChanged(ProjectElement element, string oldId, string newId)
		{
			if (ElementIdChanged != null)
				ElementIdChanged(element, oldId, newId);
		}
	}
}
