﻿using System.Windows.Input;

namespace PromoSeeker
{
    internal interface IWindowViewModel
    {
        #region Commands

        /// <summary>
        /// The command to open the window.
        /// </summary>
        ICommand OpenCommand { get; set; }

        /// <summary>
        /// The command to close the window.
        /// </summary>
        ICommand CloseCommand { get; set; }

        #endregion

        #region Methods

        /// <summary>
        /// Opens a window.
        /// </summary>
        void Open();

        /// <summary>
        /// Closes a window.
        /// </summary>
        void Close();

        #endregion
    }
}
