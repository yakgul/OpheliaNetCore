﻿using Ophelia;
using Ophelia.Service;
using Microsoft.AspNetCore.Http;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Xml;

namespace Ophelia
{
    public static class URLExtensions
    {
        public static Dictionary<string, IFormFile> FilesToUpload { get; set; }

        public static T PostURL<T>(this string URL, dynamic parameters, WebHeaderCollection headers = null, bool PreAuthenticate = false, string contentType = "application/x-www-form-urlencoded", NetworkCredential credential = null)
        {
            var sParams = "";
            if (parameters != null)
            {
                var jsonParams = Newtonsoft.Json.JsonConvert.DeserializeObject<IDictionary<string, object>>(Newtonsoft.Json.JsonConvert.SerializeObject(parameters));
                foreach (var item in jsonParams.Keys)
                {
                    if (!string.IsNullOrEmpty(sParams))
                        sParams += "&";

                    sParams += item + "=" + JsonConvert.SerializeObject(jsonParams[item]);
                }
            }
            var result = URL.PostURL(sParams, contentType, headers, PreAuthenticate, credential);
            if (!string.IsNullOrEmpty(result))
            {
                if (result.StartsWith("<"))
                {
                    XmlDocument doc = new XmlDocument();
                    doc.LoadXml(result);
                    result = JsonConvert.SerializeXmlNode(doc);
                }
            }
            return JsonConvert.DeserializeObject<T>(result);
        }
        public static T PostURL<T>(this string URL, string parameters, string contentType = "application/x-www-form-urlencoded", WebHeaderCollection headers = null, bool PreAuthenticate = false, NetworkCredential credential = null)
        {
            var result = URL.DownloadURL("POST", parameters, contentType, headers, PreAuthenticate, 120000, credential);
            if (!string.IsNullOrEmpty(result))
            {
                if (result.StartsWith("<"))
                {
                    XmlDocument doc = new XmlDocument();
                    doc.LoadXml(result);
                    result = JsonConvert.SerializeXmlNode(doc);
                }
            }
            return JsonConvert.DeserializeObject<T>(result);
        }
        public static string PostURL(this string URL, string parameters, string contentType = "application/x-www-form-urlencoded", WebHeaderCollection headers = null, bool PreAuthenticate = false, NetworkCredential credential = null)
        {
            return URL.DownloadURL("POST", parameters, contentType, headers, PreAuthenticate, 120000, credential);
        }
        public static string DownloadURL(this string URL, string method = "GET", string parameters = "", string ContentType = "application/x-www-form-urlencoded", WebHeaderCollection headers = null, bool PreAuthenticate = false, int Timeout = 120000, NetworkCredential credential = null)
        {
            byte[] postData = null;
            if (!string.IsNullOrEmpty(parameters))
            {
                if (method == "GET")
                {
                    if (URL.IndexOf("?") == -1)
                        URL += "?";
                    URL += parameters;
                }
                else
                {
                    postData = Encoding.UTF8.GetBytes(parameters);
                }
            }

            HttpWebRequest request = (HttpWebRequest)HttpWebRequest.Create(URL);
            request.Timeout = Timeout;
            request.PreAuthenticate = PreAuthenticate;
            if (credential != null)
                request.Credentials = credential;
            if (headers != null)
                request.Headers.Add(headers);
            request.Method = method;
            if (!URL.StartsWith("ftp://", StringComparison.InvariantCultureIgnoreCase))
            {
                request.ContentType = ContentType;
                if ((method == "POST" || method == "PUT") && postData != null)
                {
                    request.ContentLength = postData.Length;
                    using (var stream = request.GetRequestStream())
                    {
                        stream.Write(postData, 0, postData.Length);
                    }
                }
                else
                    request.ContentLength = 0;
            }
            FilesToUpload = null;
            return request.GetResponseWithoutException().Read();
        }
        public static TResult PostObject<T, TResult>(this string URL, T entity, dynamic parameters, WebHeaderCollection headers = null, bool PreAuthenticate = false, long languageID = 0)
        {
            var request = new WebApiObjectRequest<T>() { Data = entity };
            request.LanguageID = languageID;
            SetParameters(request, parameters);
            return URL.GetObject<T, TResult>(request, headers, PreAuthenticate);
        }
        public static ServiceObjectResult<T> PostObject<T>(this string URL, T entity, WebHeaderCollection headers = null, bool PreAuthenticate = false, long languageID = 0)
        {
            var request = new WebApiObjectRequest<T>() { Data = entity, LanguageID = languageID };
            SetParameters(request, null);
            return URL.GetObject(request, headers, PreAuthenticate);
        }
        public static ServiceObjectResult<T> GetObject<T>(this string URL, long ID, dynamic parameters = null, WebHeaderCollection headers = null, bool PreAuthenticate = false)
        {
            var request = new WebApiObjectRequest<T>() { ID = ID };
            SetParameters(request, parameters);
            return URL.GetObject(request, headers, PreAuthenticate);
        }
        public static ServiceObjectResult<T> GetObjectByParam<T>(this string URL, dynamic parameters, WebHeaderCollection headers = null, bool PreAuthenticate = false)
        {
            var request = new WebApiObjectRequest<T>();
            SetParameters(request, parameters);
            return URL.GetObject(request, headers, PreAuthenticate);
        }
        public static TResult GetObject<T, TResult>(this string URL, WebApiObjectRequest<T> request, WebHeaderCollection headers = null, bool PreAuthenticate = false)
        {
            var response = "";
            try
            {
                response = URL.PostURL(request.ToJson(), "application/json", headers, PreAuthenticate);
                return JsonConvert.DeserializeObject<TResult>(response);
            }
            catch (Exception)
            {

                throw;
            }
        }
        public static ServiceObjectResult<T> GetObject<T>(this string URL, WebApiObjectRequest<T> request, WebHeaderCollection headers = null, bool PreAuthenticate = false)
        {
            var response = "";
            try
            {
                response = URL.PostURL(request.ToJson(), "application/json", headers, PreAuthenticate);
                return JsonConvert.DeserializeObject<ServiceObjectResult<T>>(response);
            }
            catch (Exception)
            {

                throw;
            }
        }
        public static ServiceCollectionResult<T> GetCollection<T>(this string URL, int page, int pageSize, dynamic parameters = null, WebHeaderCollection headers = null, bool PreAuthenticate = false)
        {
            var request = new WebApiCollectionRequest<T>() { Page = page, PageSize = pageSize };
            SetParameters(request, parameters);
            return URL.GetCollection(request, headers, PreAuthenticate);
        }
        public static ServiceCollectionResult<T> GetCollection<T>(this string URL, int page, int pageSize, T filterEntity, dynamic parameters = null, WebHeaderCollection headers = null, bool PreAuthenticate = false)
        {
            var request = new WebApiCollectionRequest<T>() { Page = page, PageSize = pageSize, Data = filterEntity };
            SetParameters(request, parameters);
            return URL.GetCollection(request, headers, PreAuthenticate);
        }
        public static ServiceCollectionResult<T> GetCollection<T>(this string URL, WebApiCollectionRequest<T> request, WebHeaderCollection headers = null, bool PreAuthenticate = false)
        {
            var response = "";
            try
            {
                response = URL.PostURL(request.ToJson(), "application/json", headers, PreAuthenticate);
                return JsonConvert.DeserializeObject<ServiceCollectionResult<T>>(response);
            }
            catch (Exception)
            {
                throw;
            }
        }
        public static T PostURL<T, TEntity>(this string URL, WebApiObjectRequest<TEntity> request, WebHeaderCollection headers = null, bool PreAuthenticate = false)
        {
            var response = "";
            try
            {
                response = URL.PostURL(request.ToJson(), "application/json", headers, PreAuthenticate);
                return JsonConvert.DeserializeObject<T>(response);
            }
            catch (Exception)
            {

                throw;
            }
        }
        private static void SetParameters<T>(WebApiObjectRequest<T> request, dynamic parameters)
        {
            if (FilesToUpload != null)
            {
                foreach (var file in FilesToUpload.Where(op => op.Value.Length > 0))
                {
                    request.Files.Add(new FileData()
                    {
                        KeyName = file.Key,
                        FileName = file.Value.FileName,
                        ByteData = file.Value.ToByteArray()
                    });
                }
            }
            FilesToUpload = null;
            if (parameters != null)
            {
                var jsonParams = Newtonsoft.Json.JsonConvert.DeserializeObject<IDictionary<string, string>>(Newtonsoft.Json.JsonConvert.SerializeObject(parameters));
                foreach (var item in jsonParams.Keys)
                {
                    request.Parameters[item] = Convert.ToString(jsonParams[item]);
                }
            }
        }
    }
}
