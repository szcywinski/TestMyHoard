﻿using Caliburn.Micro;
using Microsoft.Phone;
using MyHoard.Models;
using MyHoard.Models.Server;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using RestSharp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO.IsolatedStorage;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;

namespace MyHoard.Services
{
    public class SynchronizationService
    {

        private readonly IEventAggregator eventAggregator;
        private readonly CollectionService collectionService;
        private readonly ConfigurationService configurationService;
        private readonly ItemService itemService;
        private readonly MediaService mediaService;
        private readonly MyHoardApi myHoardApi;
        private string backend;

        public SynchronizationService()
        {
            eventAggregator = IoC.Get<IEventAggregator>();
            collectionService = IoC.Get<CollectionService>();
            configurationService = IoC.Get<ConfigurationService>();
            backend = configurationService.Configuration.Backend;
            myHoardApi = new MyHoardApi(ConfigurationService.Backends[backend]);
            itemService = IoC.Get<ItemService>();
            mediaService = IoC.Get<MediaService>();
        }

        private async Task syncDatabase(CancellationToken cancellationToken)
        {
            RegistrationService r = new RegistrationService();
            bool refresh = await r.RefreshToken();
            if (!refresh)
            {
                configurationService.Logout();
                eventAggregator.Publish(new ServerMessage(false, Resources.AppResources.AuthenticationError));
                return;
            }

            List<Collection> collections = collectionService.CollectionList(true);

            foreach (Collection c in collections)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return;
                }

                if (!c.IsPrivate)
                {
                    string id = c.GetServerId(backend);
                    bool synced = c.IsSynced(backend);
                    bool success;
                    if (String.IsNullOrWhiteSpace(id) && !c.ToDelete)
                    {
                        success = await addCollection(c);
                        if (!success)
                            return;
                    }
                    else if (!synced && !c.ToDelete)
                    {
                        success = await modifyCollection(c);
                        if (!success)
                            return;
                    }
                    else if (c.ToDelete && !String.IsNullOrEmpty(id))
                    {
                        success = await deleteCollection(c);
                        if (!success)
                            return;
                    }

                    if (!c.IsPrivate && !c.ToDelete)
                    {
                        foreach (Item i in itemService.ItemList(c.Id, true))
                        {

                            foreach (Media m in mediaService.MediaList(i.Id, false, false))
                            {
                                id = m.GetServerId(backend);
                                if (String.IsNullOrWhiteSpace(id) && !m.ToDelete)
                                {
                                    success = await addMedia(m);
                                    if (!success)
                                        return;
                                }
                            }
                            id = i.GetServerId(backend);
                            synced = i.IsSynced(backend);
                            if (String.IsNullOrWhiteSpace(id) && !i.ToDelete)
                            {
                                success = await addItem(i, c.GetServerId(backend));
                                if (!success)
                                    return;
                            }
                            else if (!synced && !i.ToDelete)
                            {
                                success = await modifyItem(i, c.GetServerId(backend));
                                if (!success)
                                    return;
                            }
                            else if (i.ToDelete && !string.IsNullOrEmpty(id))
                            {
                                success = await deleteItem(i);
                                if (!success)
                                    return;
                            }
                        }
                    }
                }



            }

            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            List<ServerCollection> serverColections = await getCollections();
            if (serverColections != null)
            {
                foreach (ServerCollection serverCollection in serverColections)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        return;
                    }

                    Collection localCollection = collections.FirstOrDefault(c => c.GetServerId(backend) == serverCollection.id);
                    localCollection = syncCollectionFromServer(localCollection, serverCollection);

                    List<Item> items = itemService.ItemList(localCollection.Id);

                    List<ServerItem> serverItems = await getItems(serverCollection);
                    if (serverItems != null)
                        foreach (ServerItem serverItem in serverItems)
                        {
                            Item localItem = items.FirstOrDefault(i => i.GetServerId(backend) == serverItem.id);
                            syncItemFromServer(localItem, serverItem, localCollection.Id);
                            syncMedia(localItem, serverItem);
                        }
                    else
                        return;
                    foreach (Item localItem in items)
                    {
                        ServerItem serverItem = serverItems.FirstOrDefault(i => i.id == localItem.GetServerId(backend));
                        if (serverItem == null)
                        {
                            localItem.SetServerId(null, backend);
                            itemService.ModifyItem(localItem);
                            itemService.DeleteItem(localItem);
                        }
                    }
                }
            }
            else 
                return;
            eventAggregator.Publish(new ServerMessage(true, Resources.AppResources.Synchronized));
        }

        private async void syncMedia(Item localItem, ServerItem serverItem)
        {
            List<Media> media = mediaService.MediaList(localItem.Id, false);

            foreach (ServerMedia serverMedia in serverItem.media)
            {
                Media localMedia = media.FirstOrDefault(m => m.GetServerId(backend) == serverMedia.id);
                if (localMedia == null)
                {
                    byte[] image = await GetImage(serverMedia);
                    if (image != null)
                    {
                        Media m = mediaService.SavePictureToIsolatedStorage(
                            new Media()
                            {
                                Image = mediaService.ByteArrayToWriteableBitmap(image),
                                ItemId = localItem.Id
                            });
                        m.SetServerId(serverMedia.id, backend);
                        m.SetSynced(true, backend);
                        mediaService.AddMedia(m);
                    }
                    else
                        return;
                }
            }
            foreach (Media localMedia in media)
            {
                ServerMedia serverMedia = serverItem.media.FirstOrDefault(m => m.id == localMedia.GetServerId(backend));
                if (serverMedia == null)
                {
                    localMedia.SetServerId(null, backend);
                    localMedia.ToDelete = true;
                    mediaService.ModifyMedia(localMedia);
                }
            }
        }

        private async Task<byte[]> GetImage(ServerMedia serverMedia)
        {
            var request = new RestRequest("/media/" + serverMedia.id + "/", Method.GET);
            request.RequestFormat = DataFormat.Json;
            request.AddHeader("Authorization", configurationService.Configuration.AccessToken);
            request.AddHeader("Content-type", "application/json");
            var response = await myHoardApi.Execute(request);
            JObject parsedResponse = new JObject();

            switch (response.StatusCode)
            {
                case System.Net.HttpStatusCode.OK:
                    if (response.Content.StartsWith("{") || response.Content.StartsWith("["))
                    {
                        return response.RawBytes;
                    }
                    else
                        return null;
                case System.Net.HttpStatusCode.Unauthorized:
                case System.Net.HttpStatusCode.Forbidden:
                    configurationService.Logout();
                    eventAggregator.Publish(new ServerMessage(false, Resources.AppResources.AuthenticationError));
                    return null;
                default:
                    if (response.Content.StartsWith("{") || response.Content.StartsWith("["))
                        parsedResponse = JObject.Parse(response.Content);
                    eventAggregator.Publish(new ServerMessage(false, Resources.AppResources.GeneralError + ": " + parsedResponse["error_message"]
                        + "\n" + parsedResponse["errors"]));
                    return null;
            }
        }

        private void syncItemFromServer(Item localItem, ServerItem serverItem, int collectionId)
        {
            if (localItem == null)
            {
                localItem = new Item()
                {
                    Name = serverItem.name,
                    Description = serverItem.description,
                    CollectionId = collectionId,
                    LocationLat = serverItem.location.lat,
                    LocationLng = serverItem.location.lng,
                    ModifiedDate = serverItem.ModifiedDate(),
                };
                localItem.SetServerId(serverItem.id, backend);
                localItem.SetSynced(true, backend);
                itemService.AddItem(localItem);
            }
            else if (DateTime.Compare(serverItem.ModifiedDate(), localItem.ModifiedDate) > 0)
            {
                localItem.Name = serverItem.name;
                localItem.Description = serverItem.description;
                localItem.LocationLat = serverItem.location.lat;
                localItem.LocationLng = serverItem.location.lng;
                localItem.ModifiedDate = serverItem.ModifiedDate();
                localItem.SetSynced(true, backend);
                itemService.ModifyItem(localItem);
            }
        }

        private Collection syncCollectionFromServer(Collection localCollection, ServerCollection serverCollection)
        {
            if (localCollection == null)
            {
                localCollection = new Collection()
                {
                    Name = serverCollection.name,
                    Description = serverCollection.description,
                    ItemsNumber = serverCollection.items_number,
                    TagList = serverCollection.tags,
                    ModifiedDate = serverCollection.ModifiedDate()
                };
                localCollection.SetSynced(true, backend);
                localCollection.SetServerId(serverCollection.id, backend);
                localCollection = collectionService.AddCollection(localCollection);
            }
            else if (DateTime.Compare(serverCollection.ModifiedDate(), localCollection.ModifiedDate) > 0)
            {
                localCollection.Description = serverCollection.description;
                localCollection.ItemsNumber = serverCollection.items_number;
                localCollection.ModifiedDate = serverCollection.ModifiedDate();
                localCollection.Name = serverCollection.name;
                localCollection.TagList = serverCollection.tags;
                localCollection.SetSynced(true, backend);
                collectionService.ModifyCollection(localCollection);
            }
            return localCollection;
        }

        private async Task<bool> addMedia(Media m)
        {
            var request = new RestRequest("/media/", Method.POST);
            request.RequestFormat = DataFormat.Json;
            request.AddHeader("Accept", "application/json");
            request.AddHeader("Content-type", "application/json");
            request.AddHeader("Authorization", configurationService.Configuration.AccessToken);

            request.AddFile("image", mediaService.GetPictureAsByteArray(m), m.FileName);
            request.AddFile("image", mediaService.GetAbsolutePath(m.FileName));
            var response = await myHoardApi.Execute(request);

            JObject parsedResponse = new JObject();
            if (response.Content.StartsWith("{") || response.Content.StartsWith("["))
                parsedResponse = JObject.Parse(response.Content);
            switch (response.StatusCode)
            {
                case System.Net.HttpStatusCode.Created:
                    m.SetServerId((string)parsedResponse["id"], backend);
                    mediaService.ModifyMedia(m);
                    return true;
                case System.Net.HttpStatusCode.Unauthorized:
                case System.Net.HttpStatusCode.Forbidden:
                    configurationService.Logout();
                    eventAggregator.Publish(new ServerMessage(false, Resources.AppResources.AuthenticationError));
                    return false;
                default:
                    eventAggregator.Publish(new ServerMessage(false, Resources.AppResources.GeneralError + ": " + parsedResponse["error_message"]
                        + "\n" + parsedResponse["errors"]));
                    return false;
            }
        }

        private async Task<bool> deleteCollection(Collection c)
        {
            var request = new RestRequest("/collections/" + c.GetServerId(backend) + "/", Method.DELETE);
            request.RequestFormat = DataFormat.Json;
            request.AddHeader("Accept", "application/json");
            request.AddHeader("Content-type", "application/json");
            request.AddHeader("Authorization", configurationService.Configuration.AccessToken);

            var response = await myHoardApi.Execute(request);
            JObject parsedResponse = new JObject();
            if (response.Content.StartsWith("{") || response.Content.StartsWith("["))
                parsedResponse = JObject.Parse(response.Content);
            switch (response.StatusCode)
            {
                case System.Net.HttpStatusCode.NoContent:
                case System.Net.HttpStatusCode.NotFound:
                    c.SetServerId(null, backend);
                    collectionService.ModifyCollection(c);
                    collectionService.DeleteCollection(c);
                    return true;
                case System.Net.HttpStatusCode.Unauthorized:
                case System.Net.HttpStatusCode.Forbidden:
                    configurationService.Logout();
                    eventAggregator.Publish(new ServerMessage(false, Resources.AppResources.AuthenticationError));
                    return false;
                default:
                    eventAggregator.Publish(new ServerMessage(false, Resources.AppResources.GeneralError + ": " + parsedResponse["error_message"]
                        + "\n" + parsedResponse["errors"]));
                    return false;
            }
        }


        private async Task<bool> modifyCollection(Collection c)
        {
            var request = new RestRequest("/collections/" + c.GetServerId(backend) + "/", Method.PUT);
            request.RequestFormat = DataFormat.Json;
            request.AddHeader("Accept", "application/json");
            request.AddHeader("Content-type", "application/json");
            request.AddHeader("Authorization", configurationService.Configuration.AccessToken);
            request.AddBody(new { name = c.Name, description = c.Description, tags = c.TagList });

            var response = await myHoardApi.Execute(request);
            JObject parsedResponse = new JObject();
            switch (response.StatusCode)
            {
                case System.Net.HttpStatusCode.OK:
                    ServerCollection sc = JsonConvert.DeserializeObject<ServerCollection>(response.Content);
                    c.ModifiedDate = sc.ModifiedDate();
                    c.SetSynced(true, backend);
                    collectionService.ModifyCollection(c);
                    return true;
                case System.Net.HttpStatusCode.Unauthorized:
                case System.Net.HttpStatusCode.Forbidden:
                    configurationService.Logout();
                    eventAggregator.Publish(new ServerMessage(false, Resources.AppResources.AuthenticationError));
                    return false;
                default:
                    if (response.Content.StartsWith("{") || response.Content.StartsWith("["))
                        parsedResponse = JObject.Parse(response.Content);
                    eventAggregator.Publish(new ServerMessage(false, Resources.AppResources.GeneralError + ": " + parsedResponse["error_message"]
                        + "\n" + parsedResponse["errors"]));
                    return false;
            }
        }

        private async Task<bool> addCollection(Collection c)
        {
            var request = new RestRequest("/collections/", Method.POST);
            request.RequestFormat = DataFormat.Json;
            request.AddHeader("Accept", "application/json");
            request.AddHeader("Content-type", "application/json");
            request.AddHeader("Authorization", configurationService.Configuration.AccessToken);
            request.AddBody(new { name = c.Name, description = c.Description, tags = c.TagList });

            var response = await myHoardApi.Execute(request);
            JObject parsedResponse = new JObject();
            switch (response.StatusCode)
            {
                case System.Net.HttpStatusCode.Created:
                    ServerCollection sc = JsonConvert.DeserializeObject<ServerCollection>(response.Content);
                    c.SetServerId(sc.id, backend);
                    c.SetSynced(true, backend);
                    c.ModifiedDate = sc.ModifiedDate();
                    collectionService.ModifyCollection(c);
                    return true;
                case System.Net.HttpStatusCode.Unauthorized:
                case System.Net.HttpStatusCode.Forbidden:
                    configurationService.Logout();
                    eventAggregator.Publish(new ServerMessage(false, Resources.AppResources.AuthenticationError));
                    return false;
                default:
                    if (response.Content.StartsWith("{") || response.Content.StartsWith("["))
                        parsedResponse = JObject.Parse(response.Content);
                    eventAggregator.Publish(new ServerMessage(false, Resources.AppResources.GeneralError + ": " + parsedResponse["error_message"]
                        + "\n" + parsedResponse["errors"]));
                    return false;
            }
        }

        private async Task<bool> addItem(Item i, string parentServerId)
        {
            var request = new RestRequest("/items/", Method.POST);
            request.RequestFormat = DataFormat.Json;
            request.AddHeader("Accept", "application/json");
            request.AddHeader("Content-type", "application/json");
            request.AddHeader("Authorization", configurationService.Configuration.AccessToken);
            request.AddBody(new
            {
                name = i.Name,
                description = i.Description,
                location = new { lat = i.LocationLat, lng = i.LocationLng },
                media = mediaService.MediaStringList(i.Id, backend),
                collection = parentServerId
            });

            var response = await myHoardApi.Execute(request);
            JObject parsedResponse = new JObject();

            switch (response.StatusCode)
            {
                case System.Net.HttpStatusCode.Created:
                    ServerItem si = JsonConvert.DeserializeObject<ServerItem>(response.Content);
                    i.SetServerId(si.id, backend);
                    i.SetSynced(true, backend);
                    i.ModifiedDate = si.ModifiedDate();
                    itemService.ModifyItem(i);
                    return true;
                case System.Net.HttpStatusCode.Unauthorized:
                case System.Net.HttpStatusCode.Forbidden:
                    configurationService.Logout();
                    eventAggregator.Publish(new ServerMessage(false, Resources.AppResources.AuthenticationError));
                    return false;
                default:
                    if (response.Content.StartsWith("{") || response.Content.StartsWith("["))
                        parsedResponse = JObject.Parse(response.Content);
                    eventAggregator.Publish(new ServerMessage(false, Resources.AppResources.GeneralError + ": " + parsedResponse["error_message"]
                        + "\n" + parsedResponse["errors"]));
                    return false;
            }
        }

        private async Task<bool> modifyItem(Item i, string parentServerId)
        {
            var request = new RestRequest("/items/" + i.GetServerId(backend) + "/", Method.PUT);
            request.RequestFormat = DataFormat.Json;
            request.AddHeader("Accept", "application/json");
            request.AddHeader("Content-type", "application/json");
            request.AddHeader("Authorization", configurationService.Configuration.AccessToken);
            request.AddBody(new
            {
                name = i.Name,
                description = i.Description,
                location = new { lat = i.LocationLat, lng = i.LocationLng },
                media = mediaService.MediaStringList(i.Id, backend),
                collection = parentServerId
            });

            var response = await myHoardApi.Execute(request);
            JObject parsedResponse = new JObject();
            switch (response.StatusCode)
            {
                case System.Net.HttpStatusCode.OK:
                    ServerItem si = JsonConvert.DeserializeObject<ServerItem>(response.Content);
                    i.SetSynced(true, backend);
                    i.ModifiedDate = si.ModifiedDate();
                    itemService.ModifyItem(i);
                    return true;
                case System.Net.HttpStatusCode.Unauthorized:
                case System.Net.HttpStatusCode.Forbidden:
                    configurationService.Logout();
                    eventAggregator.Publish(new ServerMessage(false, Resources.AppResources.AuthenticationError));
                    return false;
                default:
                    if (response.Content.StartsWith("{") || response.Content.StartsWith("["))
                        parsedResponse = JObject.Parse(response.Content);
                    eventAggregator.Publish(new ServerMessage(false, Resources.AppResources.GeneralError + ": " + parsedResponse["error_message"]
                        + "\n" + parsedResponse["errors"]));
                    return false;
            }
        }

        private async Task<bool> deleteItem(Item i)
        {
            var request = new RestRequest("/items/" + i.GetServerId(backend) + "/", Method.DELETE);
            request.RequestFormat = DataFormat.Json;
            request.AddHeader("Accept", "application/json");
            request.AddHeader("Content-type", "application/json");
            request.AddHeader("Authorization", configurationService.Configuration.AccessToken);

            var response = await myHoardApi.Execute(request);
            JObject parsedResponse = new JObject();
            if (response.Content.StartsWith("{") || response.Content.StartsWith("["))
                parsedResponse = JObject.Parse(response.Content);
            switch (response.StatusCode)
            {
                case System.Net.HttpStatusCode.NoContent:
                case System.Net.HttpStatusCode.NotFound:
                    i.SetServerId(null, backend);
                    itemService.ModifyItem(i);
                    itemService.DeleteItem(i);
                    return true;
                case System.Net.HttpStatusCode.Unauthorized:
                case System.Net.HttpStatusCode.Forbidden:
                    configurationService.Logout();
                    eventAggregator.Publish(new ServerMessage(false, Resources.AppResources.AuthenticationError));
                    return false;
                default:
                    eventAggregator.Publish(new ServerMessage(false, Resources.AppResources.GeneralError + ": " + parsedResponse["error_message"]
                        + "\n" + parsedResponse["errors"]));
                    return false;
            }
        }

        private async Task<List<ServerItem>> getItems(ServerCollection serverCollection)
        {
            var request = new RestRequest("/collections/" + serverCollection.id + "/items/"
                + "?timestamp" + DateTime.Now.ToString(), Method.GET);
            request.RequestFormat = DataFormat.Json;
            request.AddHeader("Authorization", configurationService.Configuration.AccessToken);
            request.AddHeader("Content-type", "application/json");
            var response = await myHoardApi.Execute(request);
            JObject parsedResponse = new JObject();

            switch (response.StatusCode)
            {
                case System.Net.HttpStatusCode.OK:
                    if (response.Content.StartsWith("{") || response.Content.StartsWith("["))
                    {
                        return JsonConvert.DeserializeObject<List<ServerItem>>(response.Content);
                    }
                    else
                        return null;
                case System.Net.HttpStatusCode.Unauthorized:
                case System.Net.HttpStatusCode.Forbidden:
                    configurationService.Logout();
                    eventAggregator.Publish(new ServerMessage(false, Resources.AppResources.AuthenticationError));
                    return null;
                default:
                    if (response.Content.StartsWith("{") || response.Content.StartsWith("["))
                        parsedResponse = JObject.Parse(response.Content);
                    eventAggregator.Publish(new ServerMessage(false, Resources.AppResources.GeneralError + ": " + parsedResponse["error_message"]
                        + "\n" + parsedResponse["errors"]));
                    return null;
            }
        }

        private async Task<List<ServerCollection>> getCollections()
        {
            var request = new RestRequest("/collections/" + "?timestamp" + DateTime.Now.ToString(), Method.GET);
            request.RequestFormat = DataFormat.Json;
            request.AddHeader("Authorization", configurationService.Configuration.AccessToken);

            var response = await myHoardApi.Execute(request);
            JObject parsedResponse = new JObject();
            switch (response.StatusCode)
            {
                case System.Net.HttpStatusCode.OK:
                    if (response.Content.StartsWith("{") || response.Content.StartsWith("["))
                    {
                        return JsonConvert.DeserializeObject<List<ServerCollection>>(response.Content);
                    }
                    else
                        return null;
                case System.Net.HttpStatusCode.Unauthorized:
                case System.Net.HttpStatusCode.Forbidden:
                    configurationService.Logout();
                    eventAggregator.Publish(new ServerMessage(false, Resources.AppResources.AuthenticationError));
                    return null;
                default:
                    if (response.Content.StartsWith("{") || response.Content.StartsWith("["))
                        parsedResponse = JObject.Parse(response.Content);
                    eventAggregator.Publish(new ServerMessage(false, Resources.AppResources.GeneralError + ": " + parsedResponse["error_message"]
                        + "\n" + parsedResponse["errors"]));
                    return null;
            }
        }

        public async Task SyncDatabase(CancellationToken cancellationToken)
        {
            await Task.Factory.StartNew(() => syncDatabase(cancellationToken));
        }
    }
}
