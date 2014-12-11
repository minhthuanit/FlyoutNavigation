//  Copyright 2011  Clancey
// 
//    Licensed under the Apache License, Version 2.0 (the "License");
//    you may not use this file except in compliance with the License.
//    You may obtain a copy of the License at
// 
//        http://www.apache.org/licenses/LICENSE-2.0
// 
//    Unless required by applicable law or agreed to in writing, software
//    distributed under the License is distributed on an "AS IS" BASIS,
//    WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
//    See the License for the specific language governing permissions and
//    limitations under the License.

using System;
using System.Diagnostics;
#if __UNIFIED__
using CoreGraphics;
using Foundation;
using ObjCRuntime;
using UIKit;
#else
using System.Drawing;
using MonoTouch.CoreGraphics;
using MonoTouch.Foundation;
using MonoTouch.ObjCRuntime;
using MonoTouch.UIKit;
using CGRect=System.Drawing.RectangleF;
using CGSize=System.Drawing.SizeF;
using CGPoint=System.Drawing.PointF;
#endif
using MonoTouch.Dialog;

namespace FlyoutNavigation
{
	public enum FlyOutNavigationPosition {
		Left = 0, // default
		Right
	};

	[Register("FlyoutNavigationController")]
	public class FlyoutNavigationController : UIViewController
	{
		const float sidebarFlickVelocity = 1000.0f;
		public const int menuWidth = 280;
		//public UISearchBar SearchBar;
		UIButton closeButton;
		bool firstLaunch = true;
		FlyOutNavigationPosition position;
		DialogViewController navigation;
		public DialogViewController NavigationViewController
		{
			get { return navigation; }
		}

		protected UIView menuBorder;
		protected UIColor menuBorderColor = UIColor.LightGray;
		public UIColor MenuBorderColor
		{
			get { return menuBorderColor; }
			set { menuBorderColor = value;
				if (menuBorder != null)
				{
					menuBorder.BackgroundColor = menuBorderColor;
					menuBorder.SetNeedsDisplay();
				}
			}
		}

		protected bool showMenuBorder = false;
		public bool ShowMenuBorder
		{
			get { return showMenuBorder; }
			set { showMenuBorder = value; }
		}

		int selectedIndex;
		UIView shadowView;
		#if __UNIFIED__
		nfloat startX;
		#else
		float startX;
		#endif
		UIColor tintColor;
		UIView statusImage;
		protected UIViewController[] viewControllers;
		bool hideShadow;

		public FlyoutNavigationController(IntPtr handle) : base(handle)
		{
			Initialize();
		}

		public FlyoutNavigationController(UITableViewStyle navigationStyle = UITableViewStyle.Plain)
		{
			Initialize(navigationStyle);
		}

		public UIColor TintColor
		{
			get { return tintColor; }
			set
			{
				if (tintColor == value)
					return;
				//SearchBar.TintColor = value;
			}
		}

		public FlyOutNavigationPosition Position {
			get {
				return position;
			}
			set {
				position = value;
				shadowView.Layer.ShadowOffset = new CGSize(Position == FlyOutNavigationPosition.Left ? -5 : 5, -1);
			}
		}

		public Action SelectedIndexChanged { get; set; }

		public bool AlwaysShowLandscapeMenu { get; set; }

		public bool ForceMenuOpen { get; set; }

		private bool AlreadyLayedOut = false;

		public bool NavigationOpenedByLandscapeRotation { get; private set; }

		public bool HideShadow
		{
			get { return hideShadow; }
			set
			{
				if (value == hideShadow)
					return;
				hideShadow = value;
				if (hideShadow) {
					if (mainView != null)
						View.InsertSubviewBelow (shadowView, mainView);
				} else {
					shadowView.RemoveFromSuperview ();
				}

			}
		}

		public UIColor ShadowViewColor
		{
			get { return new UIColor(shadowView.Layer.BackgroundColor); }
			set { shadowView.Layer.BackgroundColor = value.CGColor; }
		}

		public UIViewController CurrentViewController { get; private set; }

		UIView mainView
		{
			get
			{
				if (CurrentViewController == null)
					return null;
				return CurrentViewController.View;
			}
		}

		public RootElement NavigationRoot
		{
			get { return navigation.Root; }
			set { EnsureInvokedOnMainThread(delegate { navigation.Root = value; }); }
		}

		public UITableView NavigationTableView
		{
			get { return navigation.TableView; }
		}

		public UIViewController[] ViewControllers
		{
			get { return viewControllers; }
			set
			{
				EnsureInvokedOnMainThread(delegate
					{
						viewControllers = value;
						NavigationItemSelected(GetIndexPath(SelectedIndex));
					});
			}
		}

		public bool IsOpen
		{
			get {
				if (Position == FlyOutNavigationPosition.Left) {
					return mainView.Frame.X == menuWidth;
				} else {
					return mainView.Frame.X == -menuWidth;
				}
			}
			set
			{
				if (value)
					HideMenu();
				else
					ShowMenu();
			}
		}

		bool ShouldStayOpen
		{
			get
			{
				if (ForceMenuOpen || (UIDevice.CurrentDevice.UserInterfaceIdiom == UIUserInterfaceIdiom.Pad &&
					AlwaysShowLandscapeMenu &&
					(InterfaceOrientation == UIInterfaceOrientation.LandscapeLeft
						|| InterfaceOrientation == UIInterfaceOrientation.LandscapeRight)))
					return true;
				return false;
			}
		}

		public int SelectedIndex
		{
			get { return selectedIndex; }
			set
			{
				if (selectedIndex == value)
					return;
				selectedIndex = value;
				EnsureInvokedOnMainThread(delegate { NavigationItemSelected(value); });
			}
		}

		public bool DisableRotation { get; set; }

		public override bool ShouldAutomaticallyForwardRotationMethods
		{
			get { return true; }
		}

		bool isIos7 = false;
		bool isIos8 = false;
		class UAUIView : UIView
		{
			[Export ("accessibilityIdentifier")]
			public string AccessibilityId {get;set;}
		}
		bool swapHeightAndWidthInLandscape = true;

		void Initialize(UITableViewStyle navigationStyle = UITableViewStyle.Plain)
		{
			DisableStatusBarMoving = true;
			statusImage = new UAUIView{ ClipsToBounds = true, AccessibilityId = "statusbar" };//.SetAccessibilityId( "statusbar");
			navigation = new DialogViewController(navigationStyle, null);
			var navFrame = navigation.View.Frame;
			navFrame.Width = menuWidth;
			if (Position == FlyOutNavigationPosition.Right)
				navFrame.X = mainView.Frame.Width - menuWidth;
			navigation.View.Frame = navFrame;
			View.AddSubview(navigation.View);
			//SearchBar = new UISearchBar(new CGRect(0, 0, navigation.TableView.Bounds.Width, 44))
			//	{
			//		//Delegate = new SearchDelegate (this),
			//		TintColor = TintColor
			//	};

			TintColor = UIColor.Black;
			var version = new System.Version(UIDevice.CurrentDevice.SystemVersion);
			isIos8 = version.Major >= 8;
			isIos7 = version.Major >= 7;
			if(isIos7){
				navigation.TableView.TableHeaderView = 
					new UIView (new CGRect (0, 0, 320, 22)) {
					BackgroundColor = UIColor.Clear
				};
			}
			navigation.TableView.TableFooterView = new UIView(new CGRect(0, 0, 100, 100)) {BackgroundColor = UIColor.Clear};

			// MDR 10/12/2014 - iOS 7 confuses height and width when rotated
			// MDR 10/12/2014 - Not sure about previous versions
			swapHeightAndWidthInLandscape = true;

			// MDR 10/12/2014 - iOS 8 fixes this
			if (version.Major >= 8)
				swapHeightAndWidthInLandscape = false;

			navigation.TableView.ScrollsToTop = false;
			shadowView = new UIView(){AccessibilityLabel = "flyOutShadowLayeLabel", AccessibilityIdentifier = "flyOutShadowLayer" , IsAccessibilityElement = true};

			shadowView.BackgroundColor = UIColor.Clear;
			shadowView.Layer.ShadowOffset = new CGSize(Position == FlyOutNavigationPosition.Left ? -5 : 5, -1);
			shadowView.Layer.ShadowColor = UIColor.Black.CGColor;

			shadowView.Layer.ShadowOpacity = .75f;
			closeButton = new UIButton ();
			closeButton.AccessibilityLabel = "Close Menu";
			closeButton.TouchUpInside += CloseButtonTapped;

			AlwaysShowLandscapeMenu = true;
			NavigationOpenedByLandscapeRotation = false;

			View.AddGestureRecognizer (gesture = new OpenMenuGestureRecognizer (DragContentView, shouldReceiveTouch));
		}

		void CloseButtonTapped (object sender, EventArgs e)
		{
			HideMenu(); 
		}
		OpenMenuGestureRecognizer gesture;
		public event UITouchEventArgs ShouldReceiveTouch;
		public bool DisableGesture { get; set; }
		internal bool shouldReceiveTouch(UIGestureRecognizer gesture, UITouch touch)
		{
			if (DisableGesture)
				return false;
			if (ShouldReceiveTouch != null)
				return ShouldReceiveTouch(gesture, touch);
			return true;
		}

		public override void ViewDidLayoutSubviews()
		{
			base.ViewDidLayoutSubviews();
			CGRect navFrame = View.Bounds;
			//			navFrame.Y += UIApplication.SharedApplication.StatusBarFrame.Height;
			//			navFrame.Height -= navFrame.Y;
			//this.statusbar
			navFrame.Width = menuWidth;
			if (Position == FlyOutNavigationPosition.Right)
				navFrame.X = mainView.Frame.Width - menuWidth;
			if (navigation.View.Frame != navFrame)
				navigation.View.Frame = navFrame;

			if (!AlreadyLayedOut)
			{
				AlreadyLayedOut = true;

				if (AlwaysShowLandscapeMenu && (InterfaceOrientation == UIInterfaceOrientation.LandscapeRight || InterfaceOrientation == UIInterfaceOrientation.LandscapeLeft))
					NavigationOpenedByLandscapeRotation = true;

				if (showMenuBorder)
				{
					DisplayMenuBorder(mainView.Frame);
				}
			}
		}

		public void DragContentView(UIPanGestureRecognizer panGesture)
		{
			if (ShouldStayOpen || mainView == null)
				return;
			if (!HideShadow)
				View.InsertSubviewBelow(shadowView, mainView);
			navigation.View.Hidden = false;
			CGRect frame = mainView.Frame;
			shadowView.Frame = frame;
			var translation = panGesture.TranslationInView(View).X;
			if (panGesture.State == UIGestureRecognizerState.Began)
			{
				startX = frame.X;
			}
			else if (panGesture.State == UIGestureRecognizerState.Changed)
			{
				frame.X = translation + startX;
				if (Position == FlyOutNavigationPosition.Left)
				{
					if (frame.X < 0)
						frame.X = 0;
					else if (frame.X > menuWidth)
						frame.X = menuWidth;
				}
				else
				{
					if (frame.X > 0)
						frame.X = 0;
					else if (frame.X < -menuWidth)
						frame.X = -menuWidth;
				}
				SetLocation(frame);
			}
			else if (panGesture.State == UIGestureRecognizerState.Ended)
			{
				var velocity = panGesture.VelocityInView(View).X;
				var newX = translation + startX;
				bool show = Math.Abs (velocity) > sidebarFlickVelocity ? velocity > 0 : newX > (menuWidth / 2);
				if (Position == FlyOutNavigationPosition.Right) {
					show = Math.Abs(velocity) > sidebarFlickVelocity ? velocity < 0 : newX < -(menuWidth / 2);
				}
				if (show) {
					ShowMenu ();
				} else {
					HideMenu ();
				}
			}
		}

		public override void ViewWillAppear(bool animated)
		{
			CGRect navFrame = navigation.View.Frame;
			navFrame.Width = menuWidth;
			if (Position == FlyOutNavigationPosition.Right)
				navFrame.X = mainView.Frame.Width - menuWidth;
			navFrame.Location = CGPoint.Empty;
			navigation.View.Frame = navFrame;
			View.BackgroundColor = NavigationTableView.BackgroundColor;
			var frame = mainView.Frame;
			setViewSize ();
			SetLocation (frame);
			navigation.OnSelection += NavigationItemSelected;
			base.ViewWillAppear(animated);
		}
		public override void ViewWillDisappear (bool animated)
		{
			base.ViewWillDisappear (animated);
			navigation.OnSelection -= NavigationItemSelected;
		}

		protected void NavigationItemSelected(NSIndexPath indexPath)
		{
			int index = GetIndex(indexPath);
			NavigationItemSelected(index);
		}

		protected void NavigationItemSelected(int index)
		{
			selectedIndex = index;
			if (viewControllers == null || viewControllers.Length <= index || index < 0)
			{
				if (SelectedIndexChanged != null)
					SelectedIndexChanged();
				return;
			}
			if (ViewControllers[index] == null)
			{
				if (SelectedIndexChanged != null)
					SelectedIndexChanged();
				return;
			}
			if(!DisableStatusBarMoving && !ShouldStayOpen)
				UIApplication.SharedApplication.SetStatusBarHidden(false,UIStatusBarAnimation.Fade);

			bool isOpen = false;
			if (mainView != null)
			{
				mainView.RemoveFromSuperview();
				isOpen = IsOpen;
			}
			CurrentViewController = ViewControllers[SelectedIndex];
			CGRect frame = View.Bounds;
			if (isOpen || ShouldStayOpen)
				frame.X = Position == FlyOutNavigationPosition.Left ? menuWidth : -menuWidth;

			setViewSize();
			SetLocation(frame);
			View.AddSubview(mainView);
			AddChildViewController(CurrentViewController);
			if (!ShouldStayOpen)
				HideMenu();
			if (SelectedIndexChanged != null)
				SelectedIndexChanged();
		}

		//bool isOpen {get{ return mainView.Frame.X == menuWidth; }}

		public void ShowMenu()
		{
			if (mainView == null)
				return;
			EnsureInvokedOnMainThread(delegate
				{
					//navigation.ReloadData ();
					//isOpen = true;
					navigation.View.Hidden = false;
					closeButton.Frame = mainView.Frame;
					shadowView.Frame = mainView.Frame;
					var statusFrame = UIApplication.SharedApplication.StatusBarFrame;
					statusFrame.X = mainView.Frame.X;
					statusImage.Frame = statusFrame;
					if (!ShouldStayOpen)
						View.AddSubview(closeButton);
					if (!HideShadow)
						View.InsertSubviewBelow (shadowView, mainView);
					if (ShowMenuBorder)
					{
						//menuBorder.Frame = mainView.Frame;
						//menuBorder.Frame.Width = 1f;
						View.InsertSubviewBelow(menuBorder, mainView);
					}
					UIView.BeginAnimations("slideMenu");
					UIView.SetAnimationCurve(UIViewAnimationCurve.EaseIn);
					//UIView.SetAnimationDuration(2);
					setViewSize();
					CGRect frame = mainView.Frame;
					frame.X = Position == FlyOutNavigationPosition.Left ? menuWidth : -menuWidth;
					SetLocation(frame);
					setViewSize();
					frame = mainView.Frame;
					shadowView.Frame = frame;
					closeButton.Frame = frame;
					statusFrame.X = mainView.Frame.X;
					statusImage.Frame = statusFrame;
					UIView.CommitAnimations();
				});
		}

		void setViewSize()
		{
			CGRect frame = View.Bounds;
			//frame.Location = CGPoint.Empty;
			if (ShouldStayOpen)
				frame.Width -= menuWidth;

            // mribbons@github - 28/08/2014 - Fix issue where mainview doesn't have full width sometimes after menu is opened in landscape, or app is started in landscape
            if (InterfaceOrientation == UIInterfaceOrientation.LandscapeLeft || InterfaceOrientation == UIInterfaceOrientation.LandscapeRight)
            {
				if (swapHeightAndWidthInLandscape)
				{
					frame.Width = UIScreen.MainScreen.Bounds.Height - (ShouldStayOpen ? menuWidth : 0);
					frame.Height = UIScreen.MainScreen.Bounds.Width;
				}
				else
				{
					frame.Width = UIScreen.MainScreen.Bounds.Width - (ShouldStayOpen ? menuWidth : 0);
					frame.Height = UIScreen.MainScreen.Bounds.Height;
				}
            }
            else
            {
				frame.Width = UIScreen.MainScreen.Bounds.Width - (ShouldStayOpen ? menuWidth : 0);
                frame.Height = UIScreen.MainScreen.Bounds.Height;
            }

			mainView.Bounds = frame;

			DisplayMenuBorder(mainView.Frame);
		}

		void SetLocation(CGRect frame)
		{
			mainView.Layer.AnchorPoint = new CGPoint(.5f, .5f);
			frame.Y = 0;
			if (mainView.Frame.Location == frame.Location)
				return;
			frame.Size = mainView.Frame.Size;
			var center = new CGPoint(frame.Left + frame.Width/2,
				frame.Top + frame.Height/2);
			mainView.Center = center;
			shadowView.Center = center;

			DisplayMenuBorder(frame);

			if (Math.Abs(frame.X - 0) > float.Epsilon)
			{
				getStatus();
				var statusFrame = statusImage.Frame;
				statusFrame.X = mainView.Frame.X;
				statusImage.Frame = statusFrame;
			}
		}

		bool disableStatusBarMoving;
		public bool DisableStatusBarMoving {
			get {
				if (isIos8)
					return true;
				return disableStatusBarMoving;
			}
			set {
				disableStatusBarMoving = value;
			}
		}

		private void DisplayMenuBorder(CGRect frame)
		{
			if (ShowMenuBorder && menuBorder == null)
			{
				menuBorder = new UIView();
				menuBorder.BackgroundColor = menuBorderColor;

				View.InsertSubviewAbove(menuBorder, mainView);
			}

			if (ShowMenuBorder)
			{
				CGRect borderFrame = new CGRect();
				// MDR 29/08/2014 - Prevent bottom part of border missing momentarily after rotate from landscape to portrait
				
				borderFrame.Height = UIScreen.MainScreen.Bounds.Height;
				borderFrame.Width = 1f;
				borderFrame.X = frame.X - 1f;
				//borderFrame.X = navigation.View.Frame.Right + 1f;
				borderFrame.Y = 0;
				menuBorder.Frame = borderFrame;
			}
		}

		void getStatus()
		{
			if (DisableStatusBarMoving || !isIos7 || statusImage.Superview != null || ShouldStayOpen)
				return;
			var image = captureStatusBarImage ();
			if (image == null)
				return;
			this.View.AddSubview(statusImage);
			foreach (var view in statusImage.Subviews)
				view.RemoveFromSuperview ();
			statusImage.AddSubview (image);
			statusImage.Frame = UIApplication.SharedApplication.StatusBarFrame;
			UIApplication.SharedApplication.StatusBarHidden = true;

		}
		UIView captureStatusBarImage()
		{
			try{
				UIView screenShot = UIScreen.MainScreen.SnapshotView(false);
				return screenShot;
			}
			catch(Exception ex) {
				return null;
			}
		}
		void hideStatus()
		{
			if (!isIos7)
				return;
			statusImage.RemoveFromSuperview();
			UIApplication.SharedApplication.StatusBarHidden = false;
		}

		public void HideMenu()
		{
			if (mainView == null || mainView.Frame.X == 0 || ShouldStayOpen)
				return;

			EnsureInvokedOnMainThread(delegate
				{
					//isOpen = false;
					navigation.FinishSearch();
					closeButton.RemoveFromSuperview();
					shadowView.Frame = mainView.Frame;
					var statusFrame = statusImage.Frame;
					statusFrame.X = mainView.Frame.X;
					statusImage.Frame = statusFrame;
					//UIView.AnimationWillEnd += hideComplete;
					UIView.Animate(.2,	() =>
						{
							UIView.SetAnimationCurve(UIViewAnimationCurve.EaseInOut);
							CGRect frame = View.Bounds;
							frame.X = 0;
							setViewSize();
							SetLocation(frame);
							shadowView.Frame = frame;
							statusFrame.X = 0;
							statusImage.Frame = statusFrame;
						}, hideComplete);
				});
		}

		[Export("animationEnded")]
		void hideComplete()
		{
			hideStatus();
			shadowView.RemoveFromSuperview();
			navigation.View.Hidden = true;
		}

		public void ResignFirstResponders(UIView view)
		{
			if (view.Subviews == null)
				return;
			foreach (UIView subview in view.Subviews)
			{
				if (subview.IsFirstResponder)
					subview.ResignFirstResponder();
				ResignFirstResponders(subview);
			}
		}

		public void ToggleMenu()
		{
			EnsureInvokedOnMainThread(delegate
				{
					if (!IsOpen && CurrentViewController != null && CurrentViewController.IsViewLoaded)
						ResignFirstResponders(CurrentViewController.View);
					if (IsOpen)
					{
						HideMenu();
						NavigationOpenedByLandscapeRotation = false;
					}
					else
						ShowMenu();
				});
		}

		int GetIndex(NSIndexPath indexPath)
		{
			int section = 0;
			int rowCount = 0;
			while (section < indexPath.Section)
			{
				rowCount += navigation.Root[section].Count;
				section ++;
			}
			return rowCount + indexPath.Row;
		}

		protected NSIndexPath GetIndexPath(int index)
		{
			if (navigation.Root == null)
				return NSIndexPath.FromRowSection(0, 0);
			int currentCount = 0;
			int section = 0;
			foreach (Section element in navigation.Root)
			{
				if (element.Count + currentCount > index)
					break;
				currentCount += element.Count;
				section ++;
			}

			int row = index - currentCount;
			return NSIndexPath.FromRowSection(row, section);
		}

		public override bool ShouldAutorotateToInterfaceOrientation(UIInterfaceOrientation toInterfaceOrientation)
		{
			if (DisableRotation)
				return toInterfaceOrientation == InterfaceOrientation;

			UIInterfaceOrientationMask mask = CurrentViewController.GetSupportedInterfaceOrientations();
			UIInterfaceOrientation orientation = CurrentViewController.PreferredInterfaceOrientationForPresentation();

			bool theReturn = CurrentViewController == null
				? true
				: CurrentViewController.ShouldAutorotateToInterfaceOrientation(toInterfaceOrientation);

			if (CurrentViewController != null)
				Debug.WriteLine("Should auto rotate: " + toInterfaceOrientation.ToString() + ": " + theReturn);
			else
				Debug.WriteLine("Should auto rotate: View is null");

			return theReturn;
		}

		public override UIInterfaceOrientationMask GetSupportedInterfaceOrientations()
		{
			if (CurrentViewController != null)
				return CurrentViewController.GetSupportedInterfaceOrientations();
			return UIInterfaceOrientationMask.All;
		}

		public override void WillRotate(UIInterfaceOrientation toInterfaceOrientation, double duration)
		{
			base.WillRotate(toInterfaceOrientation, duration);
		}

		public override void DidRotate(UIInterfaceOrientation fromInterfaceOrientation)
		{
			base.DidRotate(fromInterfaceOrientation);

			// mribbons@github - 28/08/2014 - Fix menu width size chunk of shadowView missing when rotating to portait mode
			shadowView.Frame = mainView.Frame;

			if (UIDevice.CurrentDevice.UserInterfaceIdiom == UIUserInterfaceIdiom.Phone)
				return;

			// mribbons@github - 28/08/2014 - Only do this is should stay open is false. 
			// Note that this doesn't seem to work well anyway, menu shows and hides, or doesn't hide when switching to portrait (can't recall which but depends how AlwaysShowLandscapeMenu and ForceMenuOpen are set)
			if (AlwaysShowLandscapeMenu)
			{
				switch (InterfaceOrientation)
				{
					case UIInterfaceOrientation.LandscapeLeft:
					case UIInterfaceOrientation.LandscapeRight:
						if (!IsOpen)
						{
							NavigationOpenedByLandscapeRotation = true;
							ShowMenu();
						}
						return;
					default:
						// mribbons@github - 28/08/2014 - Only close the menu if it was opened by rotating
						if (NavigationOpenedByLandscapeRotation)
						{
							NavigationOpenedByLandscapeRotation = false;
							HideMenu();
						}
						else
						{
							DisplayMenuBorder(mainView.Frame);
						}
						return;
				}
			}
		}

		public override void WillAnimateRotation(UIInterfaceOrientation toInterfaceOrientation, double duration)
		{
			base.WillAnimateRotation(toInterfaceOrientation, duration);
		}

		protected void EnsureInvokedOnMainThread(Action action)
		{
			if (IsMainThread())
			{
				action();
				return;
			}
			BeginInvokeOnMainThread(() =>
				action()
			);
		}

		static bool IsMainThread()
		{
			return NSThread.Current.IsMainThread;
			//return Messaging.bool_objc_msgSend(GetClassHandle("NSThread"), new Selector("isMainThread").Handle);
		}
		protected override void Dispose (bool disposing)
		{
			base.Dispose (disposing);
			SelectedIndexChanged = null;
			if(ShouldReceiveTouch != null)
			foreach (var d in ShouldReceiveTouch.GetInvocationList ())
				ShouldReceiveTouch -= (UITouchEventArgs)d;
			View.RemoveGestureRecognizer (gesture);
			closeButton.TouchUpInside -= CloseButtonTapped;
			closeButton = null;

			if (this.CurrentViewController != null) {
				this.CurrentViewController.View.RemoveFromSuperview ();
			}
		}
	}
}