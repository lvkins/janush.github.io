﻿namespace Janush
{
    /// <summary>
    /// A class containing details for the message dialog.
    /// </summary>
    public class MessageDialogBoxViewModel : BaseDialogWindowViewModel
    {
        #region Public Properties

        /// <summary>
        /// The text of the submit button.
        /// </summary>
        public string SubmitText { get; set; } = "Ok";

        #endregion
    }
}