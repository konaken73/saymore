using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using NUnit.Framework;
using Palaso.TestUtilities;
using SayMore.Model.Files;
using SayMore.Model.Files.DataGathering;

namespace SayMoreTests.model.Files.DataGathering
{
	[TestFixture]
	//[Timeout(5000)]//each gets no more than 5 seconds
	public class BackgroundFileProcessorTests
	{
		private TemporaryFolder _folder;

		[SetUp]
		public void Setup()
		{
			var r = new Random();
			_folder = new TemporaryFolder("testBackgroundFileProcessor"+r.Next());
		}

		[TearDown]
		public void TearDown()
		{
			_folder.Dispose();
		}

		[Test]
		[Category("SkipOnTeamCity")]
		public void GetAllFileData_SomeFiles_NonEmptyList()
		{
				WriteTestWav(@"blah blah");
				using (var processor = CreateProcessor())
				{
					processor.Start();
					WaitUntilNotBusy(processor);
					Assert.AreEqual(1, processor.GetAllFileData().Count());
				}
		}

		[Test]
		[Category("SkipOnTeamCity")]
		public void GetData_FileRenamed_RemovesOldGivesNew()
		{
			var original = WriteTestWav(@"first");
			using (var processor = CreateProcessor())
			{
				using (processor.ExpectNewDataAvailable())
				{
					processor.Start();
				}

				Assert.IsNotNull(processor.GetFileData(original));
				var renamed = RenameTestWav();
				WaitUntilNotBusy(processor);
				Assert.IsNull(processor.GetFileData(original));
				Assert.IsNotNull(processor.GetFileData(renamed));
			}
		}

		[Test]
		[Category("SkipOnTeamCity")]
		public void Start_OneRelevantFileExists_FiresNewDataAvailableEvent()
		{
			WriteTestWav(@"first");
			using (var processor = CreateProcessor())
			{
				using (processor.ExpectNewDataAvailable())
				{
					processor.Start();
				}
			}
		}

		[Test]
		[Category("SkipOnTeamCity")]
		public void Background_FileOverwritten_FiresNewDataAvailableEvent()
		{
			WriteTestWav(@"first");
			using (var processor = CreateProcessor())
			{
				using (processor.ExpectNewDataAvailable())
				{
					processor.Start();
				}
				using (processor.ExpectNewDataAvailable())
				{
					WriteTestWav(@"second");
				}
			}
		}

		private string WriteTestWav(string contents)
		{
			var path = _folder.Combine("test.wav");
			File.WriteAllText(path, contents);
			return path;
		}
		private string RenameTestWav()
		{
			var destPath = _folder.Combine("test1.wav");
			File.Move(_folder.Combine("test.wav"), destPath);
			return destPath;
		}

		private TestProcessor CreateProcessor()
		{
			return new TestProcessor(_folder.Path, new FileType[] { new AudioFileType(() => null) },
									  MakeDictionaryFromFile);
		}

		private void WaitUntilNotBusy(TestProcessor processor)
		{
			//give it a chance to start
			Thread.Sleep(100);
			//wait for it to end
			while(processor.Busy)
			{
				Thread.Sleep(100);
			}
		}

		private Dictionary<string, string> MakeDictionaryFromFile(string path)
		{
			var dict = new Dictionary<string, string>();
			//here, the dictionary always has just one element, the contents of the file
			//Of course, the real PresetData (which isn't the clas under test)
			//uses the sidecar file, not the media file itself.

			dict.Add("contents", File.ReadAllText(path));

			return dict;
		}
	}

	/// <summary>
	/// just gives a clean way to do this common thing in these unit tests
	/// </summary>
	public class ExpectedEvent :IDisposable
	{
		private bool _eventFired;
		public void Dispose()
		{
			// note: I wanted to just the [TimeOut] on the test fixture to time us out, but that
			// didn't work, I can't figure out why.
			var quitTime = DateTime.Now.AddSeconds(5);
			while(!_eventFired && quitTime > DateTime.Now )
			{
				Thread.Sleep(1000);
			}

			Assert.IsTrue(_eventFired,"Event did not fire in time");
		}

		public void Event(object sender, EventArgs e)
		{
			_eventFired = true;
		}
	}
	/// <summary>
	/// since BackgroundFileProcessor is abstract, this just makes a concrete thing we can test
	/// </summary>
	public class TestProcessor : BackgroundFileProcessor<Dictionary<string,string>>
	{
		public TestProcessor(string rootDirectoryPath, IEnumerable<FileType> typesOfFilesToProcess, Func<string, Dictionary<string, string>> fileDataFactory)
			: base(rootDirectoryPath, typesOfFilesToProcess, fileDataFactory)
		{
		}
		public  ExpectedEvent ExpectNewDataAvailable()
		{
			var x = new ExpectedEvent();
			NewDataAvailable += x.Event;
			return x;
		}
	}


}
