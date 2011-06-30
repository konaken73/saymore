
namespace SayMore.Transcription.Model
{
	/// ----------------------------------------------------------------------------------------
	public class TextSegment : SegmentBase, ITextSegment
	{
		string _text;

		public string Id { get; protected set; }

		/// ------------------------------------------------------------------------------------
		public TextSegment(ITier tier, string id, string text) : base(tier)
		{
			SetText(text);
			Id = id;
		}

		/// ------------------------------------------------------------------------------------
		public string GetText()
		{
			return _text;
		}

		/// ------------------------------------------------------------------------------------
		public void SetText(string text)
		{
			_text = text;
		}

		/// ------------------------------------------------------------------------------------
		public override string ToString()
		{
			return GetText();
		}
	}
}
