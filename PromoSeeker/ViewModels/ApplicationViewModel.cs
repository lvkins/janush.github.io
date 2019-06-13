﻿using PromoSeeker.Core;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Timers;
using System.Windows;

namespace PromoSeeker
{
    /// <summary>
    /// The application main view model.
    /// </summary>
    public class ApplicationViewModel : BaseViewModel
    {
        #region Private Members

        /// <summary>
        /// The currently selected product in the list.
        /// </summary>
        private ProductViewModel _selectedProduct;

        /// <summary>
        /// If the notifications popup is currently visible.
        /// </summary>
        private bool _notificationsPopupVisible;

        /// <summary>
        /// The view model for the currently shown product details.
        /// </summary>
        private ProductViewModel _currentProductDetails;

        #endregion

        #region Public Properties

        /// <summary>
        /// The added products collection.
        /// </summary>
        public ObservableCollection<ProductViewModel> Products { get; set; }
            = new ObservableCollection<ProductViewModel>();

        /// <summary>
        /// The currently selected product in the overall product list.
        /// </summary>
        public ProductViewModel SelectedProduct
        {
            get => _selectedProduct;
            set
            {
                // Update value
                _selectedProduct = value;

                // Raise property changed
                OnPropertyChanged(nameof(SelectedProduct));
            }
        }

        /// <summary>
        /// The view model for the currently shown product details.
        /// </summary>
        public ProductViewModel CurrentProductDetails
        {
            get => _currentProductDetails;
            set
            {
                // Update value
                _currentProductDetails = value;

                // Raise property changed
                OnPropertyChanged(nameof(CurrentProductDetails));
                OnPropertyChanged(nameof(ProductDetailsPageVisible));
            }
        }

        #region Notifications

        /// <summary>
        /// The notifications container.
        /// </summary>
        public NotificationsViewModel Notifications { get; set; }

        /// <summary>
        /// If the notifications popup is currently visible.
        /// </summary>
        public bool NotificationsPopupVisible
        {
            get => _notificationsPopupVisible;
            set
            {
                // Update value
                _notificationsPopupVisible = value;

                // Raise property changed
                OnPropertyChanged(nameof(NotificationsPopupVisible));
                OnPropertyChanged(nameof(AnyPopupVisible));
            }
        }

        /// <summary>
        /// The date of when the user has readed the notifications.
        /// </summary>
        public DateTime NotificationLastRead { get; set; } = DateTime.Now;

        #endregion

        /// <summary>
        /// Defines whether any popup within the main window is currently visible and present. 
        /// </summary>
        public bool AnyPopupVisible => NotificationsPopupVisible;

        /// <summary>
        /// <see langword="true"/> if the product details is shown.
        /// </summary>
        public bool ProductDetailsPageVisible => CurrentProductDetails != null;

        #endregion

        #region Public Methods

        /// <summary>
        /// Adds a new product to track to the application and stores it in the settings file.
        /// </summary>
        /// <param name="settings">The product settings object.</param>
        public void AddProduct(ProductSettings settings)
        {
            // Store the product to the settings file
            CoreDI.SettingsReader.Settings.Products.Add(settings);
            CoreDI.SettingsReader.Save();

            // Add the product to the collection
            Products.Add(new ProductViewModel(settings));
        }

        /// <summary>
        /// Deletes a single product from the application.
        /// </summary>
        /// <param name="productViewModel">The product to delete.</param>
        public void DeleteProduct(ProductViewModel productViewModel)
        {
            // Get this product in the settings
            var result = CoreDI.SettingsReader.Settings.Products.Where(_ => _.Url == productViewModel.Url).FirstOrDefault();

            // If product was found...
            if (result != null)
            {
                CoreDI.SettingsReader.Settings.Products.Remove(result);
                CoreDI.SettingsReader.Save();
            }

            // Cleanup
            productViewModel.Product?.Dispose();

            // Remove from the collection
            Products.Remove(productViewModel);
        }

        /// <summary>
        /// Loads the application content state.
        /// </summary>
        public void Load()
        {
            try
            {
                // Load user settings here to catch all the exceptions and handle corrupted
                // user settings in one place
                CoreDI.SettingsReader.Load();

                // Get the stored products list
                var products = CoreDI.SettingsReader.Settings.Products;

                // Iterate over products list
                products.OrderBy(_ => !_.Tracked)
                    .ToList()
                    // Add the product to the collection
                    .ForEach(product => Products.Add(new ProductViewModel(product)));
            }
            catch (Exception ex)
            {
                // Let developer know
                Debugger.Break();

                // Unable to load application 
                CoreDI.Logger.Fatal("Load failed");
                CoreDI.Logger.Exception(ex);

                // Show error to the user
                DI.UIManager.ShowMessageDialogBoxAsync(new MessageDialogBoxViewModel
                {
                    Type = DialogBoxType.Error,
                    Message = "Application state wasn't loaded properly, please ensure your settings file is not corrupted.", // TODO: localize me
                });

                // TODO: Ask user to restore backed up settings file if we have one
            }
        }

        /// <summary>
        /// Saves the application content state.
        /// </summary>
        public void Save()
        {
            // Write diagnostic info
            Debug.WriteLine($"Save application state");

            // Update products
            CoreDI.SettingsReader.Settings.Products = Products.Select(_ =>
            {
                // Update stored settings values with the values in view model
                _.Settings.Url = _.Url;
                _.Settings.Name = _.OriginalName;
                _.Settings.Price = _.Product.PriceInfo;
                _.Settings.Culture = _.Culture;
                _.Settings.DisplayName = _.DisplayName;
                _.Settings.Tracked = _.Tracked;
                _.Settings.NameHistory = _.NameHistory;
                _.Settings.PriceHistory = _.PriceHistory;
                _.Settings.LastChecked = _.LastCheck;
                _.Settings.Created = _.DateAdded;

                // Return updates settings
                return _.Settings;
            }).ToList();

            // Save settings
            CoreDI.SettingsReader.Save();
        }

        #region Notifications

        /// <summary>
        /// Loads the recent notifications.
        /// </summary>
        public void RefreshNotifications()
        {
            // If notifications are not yet initialized...
            if (Notifications == null)
            {
                // Initialize
                Notifications = new NotificationsViewModel();
            }

            // Reset notification status
            Notifications.New = false;

            // Indicate no new notifications in tray
            DI.UIManager.Tray.Indicate(false);

            // Load notifications
            Notifications.Load();

            // Set current read time to mark as readed
            NotificationLastRead = DateTime.Now;

            // Raise property changed event
            OnPropertyChanged(nameof(Notifications));
        }

        /// <summary>
        /// Toggles the notifications popup visibility.
        /// </summary>
        public void ToggleNotifications()
        {
            // Reload
            RefreshNotifications();

            // Toggle visibility
            NotificationsPopupVisible = !NotificationsPopupVisible;
        }

        /// <summary>
        /// Handles a single notification message in the application.
        /// </summary>
        /// <param name="message">The notification message.</param>
        /// <param name="product">The product that notification refer to.</param>
        /// <param name="popToast">Whether to show the toast notification.</param>
        public void NotificationReceived(string message, ProductViewModel product = null,
            NotificationType notificationType = NotificationType.None, bool popToast = true)
        {
            // If we have a default notification...
            // NOTE: Right now we only handle default notifications that are displayed in the popup
            if (notificationType == NotificationType.None)
            {
                // If notifications are not yet initialized...
                if (Notifications == null)
                {
                    // Initialize
                    Notifications = new NotificationsViewModel();

                    // Raise property changed event
                    OnPropertyChanged(nameof(Notifications));
                }

                // Flag that new notification is available
                Notifications.New = true;

                // Indicate new notifications
                DI.UIManager.Tray.Indicate(true);
            }

            // If we have a message to notify and should show a toast notification...
            if (!string.IsNullOrEmpty(message) && popToast)
            {
                DI.UIManager.Tray.Notification(message, product?.Name);
            }
        }

        #endregion

        #region Window Handling

        /// <summary>
        /// Shows a children window to the user.
        /// </summary>
        /// <param name="onClose">The action to be executed when the window was closed.</param>
        /// <typeparam name="T">The window type to be created.</typeparam>
        public void ShowWindow<T>(BaseViewModel viewModel, Action onClose = null)
            where T : Window
        {
            // Attempt to find the window in the currently initialized windows in our application
            var window = Application.Current.Windows.OfType<T>().FirstOrDefault();

            // If none window was found...
            if (window == null)
            {
                // Create new window
                window = Activator.CreateInstance<T>();
                window.Title = $"{Consts.APP_TITLE} • {window.Title}";
                window.Owner = Application.Current.MainWindow;
                window.DataContext = viewModel;
                window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
                //window.ResizeMode = ResizeMode.NoResize;

                // If we have close callback...
                if (onClose != null)
                {
                    window.Closed += (s, e) => onClose();
                }
            }

            // Show
            window.ShowDialog();
        }

        /// <summary>
        /// Closes all windows of a given type.
        /// </summary>
        /// <typeparam name="T">The type of a window to be closed.</typeparam>
        public void CloseAllWindow<T>()
            where T : Window
        {
            Application.Current.Windows.OfType<T>().ToList().ForEach(_ => _.Close());
        }

        #endregion

        #endregion
    }
}
