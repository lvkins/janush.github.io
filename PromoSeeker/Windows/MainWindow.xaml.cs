﻿using System.Windows;

namespace PromoSeeker
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();

            // Link with the view model
            DataContext = new WindowViewModel(this);
        }
    }
}