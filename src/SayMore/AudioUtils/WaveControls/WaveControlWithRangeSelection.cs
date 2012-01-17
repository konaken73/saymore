using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace SayMore.AudioUtils
{
	public class WaveControlWithRangeSelection : WaveControlWithMovableBoundaries
	{
		public delegate void SelectedRegionChangedHandler(WaveControlWithRangeSelection wavCtrl,
			TimeSpan newStart, TimeSpan newEnd);

		public event SelectedRegionChangedHandler SelectedRegionChanged;

		/// ------------------------------------------------------------------------------------
		protected override WavePainterBasic GetNewWavePainter(IEnumerable<float> samples, TimeSpan totalTime)
		{
			return new WavePainterWithRangeSelection(this, samples, totalTime);
		}

		/// ------------------------------------------------------------------------------------
		private WavePainterWithRangeSelection Painter
		{
			get { return _painter as WavePainterWithRangeSelection; }
		}

		/// ------------------------------------------------------------------------------------
		public void PlaySelectedRegion()
		{
			if (Painter.SelectedRegionStartTime < Painter.SelectedRegionEndTime)
				base.Play(Painter.SelectedRegionStartTime, Painter.SelectedRegionEndTime);
		}

		/// ------------------------------------------------------------------------------------
		public void InvalidateSelectedRegion()
		{
			if (Painter.SelectedRegionStartTime == Painter.SelectedRegionEndTime)
				return;

			Invalidate(new Rectangle(Painter.ConvertTimeToXCoordinate(Painter.SelectedRegionStartTime),
				0, Painter.ConvertTimeToXCoordinate(Painter.SelectedRegionEndTime), ClientSize.Height));
		}

		/// ------------------------------------------------------------------------------------
		public int GetSegmentForX(int dx)
		{
			var timeAtX = GetTimeFromX(dx);

			int segNumber = 0;

			foreach (var boundary in SegmentBoundaries)
			{
				if (timeAtX <= boundary)
					return segNumber;

				segNumber++;
			}

			return -1;
		}

		/// ------------------------------------------------------------------------------------
		public void ClearSelection()
		{
			SetSelectionTimes(TimeSpan.Zero, TimeSpan.Zero);
		}

		/// ------------------------------------------------------------------------------------
		public void SetSelectionTimes(TimeSpan start, TimeSpan end)
		{
			var regionChanged = (start != Painter.SelectedRegionStartTime ||
				end != Painter.SelectedRegionEndTime);

			Painter.SetSelectionTimes(start, end);

			if (regionChanged && SelectedRegionChanged != null)
				SelectedRegionChanged(this, start, end);
		}

		/// ------------------------------------------------------------------------------------
		protected override void OnMouseDown(MouseEventArgs e)
		{
			base.OnMouseDown(e);

			if (_boundaryMouseOver != default(TimeSpan))
				return;

			var segNumber = GetSegmentForX(e.X);
			if (segNumber < 0)
				return;

			var start = (segNumber == 0 ? TimeSpan.Zero : SegmentBoundaries.ElementAt(segNumber - 1));
			SetSelectionTimes(start, SegmentBoundaries.ElementAt(segNumber));
		}

		/// ------------------------------------------------------------------------------------
		protected override void OnBoundaryMouseDown(int mouseX, TimeSpan boundaryClicked,
			int indexOutOfBoundaryClicked)
		{
			var startTime = (indexOutOfBoundaryClicked == 0 ? TimeSpan.Zero :
				SegmentBoundaries.ElementAt(indexOutOfBoundaryClicked - 1));

			Painter.SetSelectionTimes(startTime, boundaryClicked);
			base.OnBoundaryMouseDown(mouseX, boundaryClicked, indexOutOfBoundaryClicked);
		}

		/// ------------------------------------------------------------------------------------
		protected override void OnBoundaryMoving(TimeSpan newBoundary)
		{
			base.OnBoundaryMoving(newBoundary);
			Painter.SetSelectionTimes(Painter.SelectedRegionStartTime, newBoundary);
		}

		/// ------------------------------------------------------------------------------------
		protected override void OnBoundaryMoved(TimeSpan oldBoundary, TimeSpan newBoundary)
		{
			base.OnBoundaryMoved(oldBoundary, newBoundary);
			SetSelectionTimes(Painter.SelectedRegionStartTime, newBoundary);
		}
	}
}