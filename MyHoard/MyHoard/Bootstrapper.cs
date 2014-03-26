﻿using Caliburn.Micro;
using Caliburn.Micro.BindableAppBar;
using MyHoard.Services;
using MyHoard.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Controls;


namespace MyHoard
{
   
    public class Bootstrapper : PhoneBootstrapper
    {
        PhoneContainer container;

        protected override void Configure()
        {
            if (Execute.InDesignMode)
                return;

            container = new PhoneContainer();

            container.RegisterPhoneServices(RootFrame);
            container.PerRequest<MainPageViewModel>();
            container.PerRequest<AddCollectionViewModel>();
            container.PerRequest<CollectionListViewModel>();
            container.PerRequest<CollectionDetailsViewModel>();
            container.PerRequest<AddItemViewModel>();
            container.PerRequest<ItemDetailsViewModel>();
            container.PerRequest<PictureViewModel>();
            container.PerRequest<PhotoChooserViewModel>();
            container.PerRequest<SettingsViewModel>();
            container.PerRequest<RegisterViewModel>();
            container.PerRequest<LoginViewModel>();
            container.Singleton<DatabaseService>();
            container.Singleton<CollectionService>();
            container.Singleton<ItemService>();
            container.Singleton<MediaService>();
            container.Singleton<ConfigurationService>();
            
            AddCustomConventions();
                
        }

        static void AddCustomConventions()
        {
            ConventionManager.AddElementConvention<BindableAppBarButton>(
            Control.IsEnabledProperty, "DataContext", "Click");
        }

        protected override object GetInstance(Type service, string key)
        {
            return container.GetInstance(service, key);
        }

        protected override IEnumerable<object> GetAllInstances(Type service)
        {
            return container.GetAllInstances(service);
        }

        protected override void BuildUp(object instance)
        {
            container.BuildUp(instance);
        }
        protected override void OnLaunch(object sender, Microsoft.Phone.Shell.LaunchingEventArgs e)
        {
            base.OnLaunch(sender, e);
            IoC.Get<MediaService>().CleanIsolatedStorage();
        }
        protected override void OnClose(object sender, Microsoft.Phone.Shell.ClosingEventArgs e)
        {
            ConfigurationService configurationService = IoC.Get<ConfigurationService>();
            if(configurationService.Configuration.KeepLogged)
            {
                RegistrationService rs = new RegistrationService();
                rs.Login(configurationService.Configuration.UserName, configurationService.Configuration.Password, true, configurationService.Configuration.Backend);
            }
            else
            {
                configurationService.Configuration.IsLoggedIn = false;
                configurationService.SaveConfig();
            }

            
            base.OnClose(sender, e);
        }

           
    }
        
    
}
