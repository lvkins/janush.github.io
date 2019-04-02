﻿using System.Collections.ObjectModel;

namespace PromoSeeker
{
    /// <summary>
    /// The design-time data model for a <see cref="AddProductViewModel"/>.
    /// </summary>
    public class AddProductDesignModel : AddProductViewModel
    {
        #region Singleton

        /// <summary>
        /// A single instance of the design model.
        /// </summary>
        public static AddProductDesignModel Instance = new AddProductDesignModel();

        #endregion

        #region Constructor

        /// <summary>
        /// Default constructor
        /// </summary>
        public AddProductDesignModel()
        {
            StepTwo = false;
            Status = "Heads up! This is a design time message!";
        }

        #endregion
    }
}
