using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace TTMulti
{
    /// <summary>
    /// A group of controllers that contains one or more pairs of left and right controllers.
    /// </summary>
    sealed class ControllerGroup : IDisposable
    {
        /// <summary>
        /// The Toontown window of a controller in this group was activated
        /// </summary>
        public event EventHandler ControllerWindowActivated;

        /// <summary>
        /// The Toontown window of a controller in this group was deactivated
        /// </summary>
        public event EventHandler ControllerWindowDeactivated;

        /// <summary>
        /// The Toontown window of a controller in this group was closed
        /// </summary>
        public event EventHandler ControllerWindowHandleChanged;

        /// <summary>
        /// A controller in this group should be activated (due to a mouse click)
        /// </summary>
        public event EventHandler ControllerShouldActivate;

        /// <summary>
        /// A pair of controllers was added or removed
        /// </summary>
        public event EventHandler PairAddedRemoved;

        internal List<ControllerPair> ControllerPairs { get; } = new List<ControllerPair>();

        internal IEnumerable<ToontownController> AllControllers { get => ControllerPairs.SelectMany(p => p.AllControllers); }
        internal IEnumerable<ToontownController> LeftControllers { get => ControllerPairs.Select(p => p.LeftController); }
        internal IEnumerable<ToontownController> RightControllers { get => ControllerPairs.Select(p => p.RightController); }

        internal int GroupNumber { get; }

        public ControllerGroup(int groupNumber)
        {
            GroupNumber = groupNumber;

            AddPair();
        }

        // No finalizer: cleanup (closing the border/overlay WinForms windows and unsubscribing events) must run on
        // the UI thread via an explicit Dispose(), never on the GC finalizer thread where it can crash (CORR-11).

        /// <summary>
        /// Add a new ControllerPair at the end of the list
        /// </summary>
        /// <returns></returns>
        public ControllerPair AddPair()
        {
            var pair = new ControllerPair(GroupNumber, ControllerPairs.Count + 1);

            pair.LeftController.WindowActivated += Controller_WindowActivated;
            pair.RightController.WindowActivated += Controller_WindowActivated;
            pair.LeftController.WindowDeactivated += Controller_WindowDeactivated;
            pair.RightController.WindowDeactivated += Controller_WindowDeactivated;
            pair.LeftController.WindowHandleChanged += Controller_WindowHandleChanged;
            pair.RightController.WindowHandleChanged += Controller_WindowHandleChanged;
            pair.LeftController.ShouldActivate += Controller_ShouldActivate;
            pair.RightController.ShouldActivate += Controller_ShouldActivate;

            ControllerPairs.Add(pair);

            PairAddedRemoved?.Invoke(this, EventArgs.Empty);

            return pair;
        }

        /// <summary>
        /// Remove the last ControllerPair from the list
        /// </summary>
        public void RemoveLastPair()
        {
            if (ControllerPairs.Count > 1)
            {
                ControllerPair pair = ControllerPairs.Last();

                pair.Shutdown();

                ControllerPairs.Remove(pair);

                PairAddedRemoved?.Invoke(this, EventArgs.Empty);
            }
        }

        private void Controller_WindowHandleChanged(object sender, EventArgs e)
        {
            ControllerWindowHandleChanged?.Invoke(sender, e);
        }

        private void Controller_WindowDeactivated(object sender, EventArgs e)
        {
            ControllerWindowDeactivated?.Invoke(sender, e);
        }

        private void Controller_WindowActivated(object sender, EventArgs e)
        {
            ControllerWindowActivated?.Invoke(sender, e);
        }

        private void Controller_ShouldActivate(object sender, EventArgs e)
        {
            ControllerShouldActivate?.Invoke(sender, e);
        }

        public void Dispose()
        {
            foreach (var pair in ControllerPairs)
            {
                pair.Shutdown();
            }

            GC.SuppressFinalize(this);
        }
    }
}
