﻿using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System;

namespace PromoSeeker
{
    /// <summary>
    /// The extension methods for use in the <see cref="DI"/>.
    /// </summary>
    internal static class DIExtensions
    {
        /// <summary>
        /// 
        /// </summary>
        /// <param name="services"></param>
        /// <param name="configure"></param>
        public static void AddConfiguration(this IServiceCollection services, Action<IConfigurationBuilder> configure)
        {
            // Create the configuration builder
            var configurationBuilder = new ConfigurationBuilder();

            // Run action to configure
            configure(configurationBuilder);

            // Build the configuration and add to the services
            services.AddSingleton<IConfiguration>(configurationBuilder.Build());
        }


        /// <summary>
        /// Adds the view model services into the <paramref name="services"/>.
        /// </summary>
        /// <param name="services"></param>
        public static void AddViewModels(this IServiceCollection services)
        {
            services.AddSingleton<ApplicationViewModel>();
            services.AddSingleton<AddProductViewModel>();
            services.AddSingleton<LogsViewModel>();
        }
    }
}
