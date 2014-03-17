﻿using Caliburn.Micro;
using MyHoard.Services;
using MyHoard.Views;
using Newtonsoft.Json.Linq;
using RestSharp;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace MyHoard.ViewModels
{
    public class RegisterViewModel : ViewModelBase, IHandle<IRestResponse>, IHandle<ServiceErrorMessage>
    {

        private readonly IEventAggregator eventAggregator;
        private Dictionary<string, string> backends;
        private bool canRegister;
        private bool isFormAccessible;
        private Visibility arePasswordRequirementsVisible;
        private Visibility isProgressBarVisible;
        private string userName;
        private string email;
        private string selectedBackend;
        private PasswordBox passwordBox;
        private PasswordBox confirmPasswordBox;
        private RestRequestAsyncHandle asyncHandle;

        public RegisterViewModel(INavigationService navigationService, CollectionService collectionService, IEventAggregator eventAggregator)
            : base(navigationService, collectionService)
        {
            Backends = new Dictionary<string, string>()
            {
                {"Python","http://78.133.154.18:8080"},
                {"Java1","http://78.133.154.39:1080"},
                {"Java2","http://78.133.154.39:2080"}
            };
            SelectedBackend = Backends.Keys.First();
            this.eventAggregator = eventAggregator;
            eventAggregator.Subscribe(this);
            IsFormAccessible = true;
        }

        public void OnGoBack(CancelEventArgs eventArgs)
        {
            if (!IsFormAccessible)
            {
                MessageBoxResult messageResult = MessageBox.Show(Resources.AppResources.CancelConfirm, "", MessageBoxButton.OKCancel);
                if (messageResult == MessageBoxResult.Cancel)
                {
                    eventArgs.Cancel = true;
                }
                else
                {
                    asyncHandle.Abort();
                }
            }
        }

        public void Handle(IRestResponse response)
        {
            IsFormAccessible = true;
            CanRegister = true;
            if (response.StatusCode == System.Net.HttpStatusCode.Created)
            {
                MessageBox.Show(Resources.AppResources.UserCreated);
                NavigationService.GoBack();
            }
            else
            {
                string message = Resources.AppResources.ErrorOccurred;
                if (!String.IsNullOrEmpty(response.Content))
                {
                    try
                    {
                        JObject parsedResponse = JObject.Parse(response.Content);
                        message += ": " + parsedResponse["error_message"];
                    }
                    catch (Exception e)
                    {
                        Debug.WriteLine(e.Message);
                    }
                }
                MessageBox.Show(message);
            }
        }

        public void Handle(ServiceErrorMessage message)
        {
            IsFormAccessible = true; ;
            CanRegister = true;
            MessageBox.Show(message.Content);
        }


        protected override void OnDeactivate(bool close)
        {
            eventAggregator.Unsubscribe(this);
            base.OnDeactivate(close);
        }


        protected override void OnActivate()
        {
            eventAggregator.Subscribe(this);
            base.OnActivate();
        }


        protected override void OnViewLoaded(object view)
        {
            passwordBox = ((RegisterView)view).Password;
            confirmPasswordBox = ((RegisterView)view).ConfirmPassword;
            passwordBox.PasswordChanged += new RoutedEventHandler(PasswordChanged);
            confirmPasswordBox.PasswordChanged += new RoutedEventHandler(PasswordChanged);
            base.OnViewLoaded(view);
        }

        public void PasswordChanged(object sender, RoutedEventArgs e)
        {

            if (!String.IsNullOrEmpty(passwordBox.Password) &&
                Regex.IsMatch(passwordBox.Password, "^(?=.*[A-Z])(?=.*[a-z])(?=.*[0-9])(?=.*[^A-Za-z0-9]).{5,}$", RegexOptions.IgnoreCase))
            {
                ArePasswordRequirementsVisible = Visibility.Collapsed;
            }
            else
            {
                ArePasswordRequirementsVisible = Visibility.Visible;
            }
            DataChanged();

        }

        public void DataChanged()
        {
            CanRegister = (!String.IsNullOrWhiteSpace(UserName) && !String.IsNullOrEmpty(Email) &&
                Regex.IsMatch(Email, @"^(?("")(""[^""]+?""@)|(([0-9a-z]((\.(?!\.))|[-!#\$%&'\*\+/=\?\^`\{\}\|~\w])*)(?<=[0-9a-z])@))" +
                @"(?(\[)(\[(\d{1,3}\.){3}\d{1,3}\])|(([0-9a-z][-\w]*[0-9a-z]*\.)+[a-z0-9]{2,24}))$", RegexOptions.IgnoreCase) &&
                ArePasswordRequirementsVisible == Visibility.Collapsed && passwordBox.Password == confirmPasswordBox.Password);
        }

        public async void Register()
        {
            IsFormAccessible = false;
            RegistrationService registrationService = new RegistrationService(Backends[SelectedBackend]);
            asyncHandle = registrationService.Register(UserName, Email, passwordBox.Password);
        }

        public string UserName
        {
            get { return userName; }
            set
            {
                userName = value;
                NotifyOfPropertyChange(() => UserName);
            }
        }

        public string Email
        {
            get { return email; }
            set
            {
                email = value;
                NotifyOfPropertyChange(() => Email);
            }
        }

        public string SelectedBackend
        {
            get { return selectedBackend; }
            set
            {
                selectedBackend = value;
                NotifyOfPropertyChange(() => SelectedBackend);
            }
        }

        public bool CanRegister
        {
            get { return canRegister; }
            set
            {
                canRegister = value;
                NotifyOfPropertyChange(() => CanRegister);
            }
        }

        public bool IsFormAccessible
        {
            get { return isFormAccessible; }
            set
            {
                isFormAccessible = value;
                NotifyOfPropertyChange(() => IsFormAccessible);
                if (!value)
                    CanRegister = false;
                IsProgressBarVisible = (IsFormAccessible ? Visibility.Collapsed : Visibility.Visible);
            }
        }

        public Visibility IsProgressBarVisible
        {
            get { return isProgressBarVisible; }
            set
            {
                isProgressBarVisible = value;
                NotifyOfPropertyChange(() => IsProgressBarVisible);
            }
        }

        public Visibility ArePasswordRequirementsVisible
        {
            get { return arePasswordRequirementsVisible; }
            set
            {
                arePasswordRequirementsVisible = value;
                NotifyOfPropertyChange(() => ArePasswordRequirementsVisible);
            }
        }

        public Dictionary<string, string> Backends
        {
            get { return backends; }
            set
            {
                backends = value;
                NotifyOfPropertyChange(() => Backends);
            }
        }

    }
}
