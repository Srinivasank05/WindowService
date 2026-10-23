using System;
using System.Collections.Generic;
using System.Text;

namespace WorkingService
{
    public class ServiceHelper
    {
        private static IConfiguration _config;
        private readonly HttpClient _http;
        //private string UptUrl => _config[$"{ConfigSection}:UptUrl"]!;


        

        public static void Init(IConfiguration config)
        {
            _config = config;
        }

        //public static string GetAppSettingAttributeValue(string ConfigSection, string attributeName)
        //{ 
        //  return _config[$"{ConfigSection}:{attributeName}"]!;
        //}
        public static string GetAppSettingAttributeValue( string ConfigSection,  string attributeName)
        {
            return _config[$"{ConfigSection}:{attributeName}"]!;
        }

    }
}
