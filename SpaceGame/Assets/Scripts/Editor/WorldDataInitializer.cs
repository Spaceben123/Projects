using UnityEngine;
using UnityEditor;
using System.IO;

public class WorldDataInitializer
{
    [MenuItem("SpaceGame/Initialize World Data")]
    public static void InitializeAll()
    {
        Directory.CreateDirectory("Assets/Resources/Regions");
        Directory.CreateDirectory("Assets/Resources/Cities");
        Directory.CreateDirectory("Assets/Resources/LaunchSites");

        WriteRegions();
        WriteCities();
        WriteLaunchSites();

        AssetDatabase.Refresh();
        Debug.Log("[WorldData] All data files written to Assets/Resources/.");
    }

    static void W(string path, string json) => File.WriteAllText(path, json);

    static string R(string id, string name, int align, bool nuclear, float wealth,
                    float hawk, float capLat, float capLon, float pop, float[] bnd)
    {
        string bndStr = "[" + string.Join(",", bnd) + "]";
        return $"{{\"regionId\":\"{id}\",\"displayName\":\"{name}\",\"defaultAlignment\":{align}," +
               $"\"isNuclearPower\":{(nuclear?"true":"false")},\"baseWealth\":{wealth}," +
               $"\"hawkishness\":{hawk},\"capitalLat\":{capLat},\"capitalLon\":{capLon}," +
               $"\"startingPopulationM\":{pop},\"boundary\":{bndStr}}}";
    }

    static void WriteRegions()
    {
        string p = "Assets/Resources/Regions/";
        W(p+"north_america.json", R("north_america","North America",0,true,0.85f,0.65f,38.9f,-77.0f,500f,
            new float[]{71,-141,71,-52,42,-52,25,-77,16,-92,32,-117,60,-141}));
        W(p+"c_america.json", R("c_america","Central America",2,false,0.35f,0.25f,19.43f,-99.13f,180f,
            new float[]{32,-117,25,-77,16,-92,8,-77,8,-83,22,-106,32,-117}));
        W(p+"s_america.json", R("s_america","South America",2,false,0.45f,0.30f,-15.8f,-47.9f,450f,
            new float[]{12,-72,0,-50,-5,-35,-34,-53,-56,-68,-18,-75,0,-78,12,-72}));
        W(p+"w_europe.json", R("w_europe","West Europe",0,true,0.88f,0.40f,50.85f,4.35f,450f,
            new float[]{71,-25,71,25,35,25,35,-8,36,-8,36,-25,71,-25}));
        W(p+"e_europe.json", R("e_europe","East Europe",0,false,0.55f,0.55f,52.2f,21.0f,180f,
            new float[]{71,15,71,25,45,25,45,15,71,15}));
        W(p+"russia.json", R("russia","Russia",1,true,0.58f,0.75f,55.75f,37.62f,145f,
            new float[]{72,28,72,180,50,180,42,130,38,60,50,28,72,28}));
        W(p+"middle_east.json", R("middle_east","Middle East",2,true,0.52f,0.70f,33.34f,44.40f,400f,
            new float[]{37,26,37,65,12,45,12,32,22,30,30,26,37,26}));
        W(p+"n_africa.json", R("n_africa","North Africa",2,false,0.28f,0.40f,30.05f,31.24f,280f,
            new float[]{38,-6,38,37,12,45,5,42,5,-18,35,-6,38,-6}));
        W(p+"s_africa.json", R("s_africa","South Africa",2,false,0.25f,0.30f,-25.7f,28.2f,700f,
            new float[]{5,-18,5,50,-35,50,-35,-20,5,-18}));
        W(p+"e_asia.json", R("e_asia","East Asia",1,true,0.72f,0.70f,39.91f,116.39f,1500f,
            new float[]{55,100,55,145,20,122,20,100,55,100}));
        W(p+"s_asia.json", R("s_asia","South Asia",2,true,0.30f,0.60f,28.67f,77.22f,2000f,
            new float[]{38,60,38,100,5,80,12,42,38,60}));
        W(p+"se_asia.json", R("se_asia","Southeast Asia",2,false,0.38f,0.25f,13.75f,100.50f,700f,
            new float[]{20,92,20,142,-10,141,-10,95,5,100,20,92}));
        W(p+"c_asia.json", R("c_asia","Central Asia",2,false,0.32f,0.35f,51.18f,71.45f,80f,
            new float[]{55,50,55,90,37,78,37,50,55,50}));
        W(p+"oceania.json", R("oceania","Oceania",0,false,0.75f,0.30f,-35.31f,149.12f,45f,
            new float[]{-10,110,-10,180,-50,180,-50,110,-10,110}));
    }

    static void WriteCities()
    {
        var rows = new string[]
        {
            "Tokyo,35.68,139.69,37.4,e_asia",
            "Delhi,28.67,77.22,32.9,s_asia",
            "Shanghai,31.23,121.47,28.5,e_asia",
            "Dhaka,23.72,90.41,23.2,s_asia",
            "Sao Paulo,-23.55,-46.63,22.4,s_america",
            "Mexico City,19.43,-99.13,22.1,c_america",
            "Cairo,30.05,31.24,21.3,n_africa",
            "Beijing,39.91,116.39,21.2,e_asia",
            "Mumbai,19.07,72.87,20.7,s_asia",
            "Osaka,34.69,135.50,19.1,e_asia",
            "New York,40.71,-74.01,18.8,n_america",
            "Chongqing,29.56,106.55,18.7,e_asia",
            "Karachi,24.86,67.01,17.2,s_asia",
            "Istanbul,41.01,28.95,15.8,middle_east",
            "Lagos,6.52,3.38,15.3,n_africa",
            "Kinshasa,-4.32,15.32,15.1,s_africa",
            "Buenos Aires,-34.60,-58.38,15.5,s_america",
            "Kolkata,22.57,88.36,15.1,s_asia",
            "Manila,14.60,120.98,14.5,se_asia",
            "Guangzhou,23.13,113.26,14.0,e_asia",
            "Tianjin,39.13,117.18,9.2,e_asia",
            "Moscow,55.75,37.62,13.0,russia",
            "Shenzhen,22.54,114.06,12.8,e_asia",
            "Los Angeles,34.05,-118.24,12.4,n_america",
            "Lahore,31.56,74.34,13.1,s_asia",
            "Bangalore,12.97,77.59,12.7,s_asia",
            "Jakarta,-6.21,106.85,11.0,se_asia",
            "Bogota,4.71,-74.07,11.3,s_america",
            "Lima,-12.05,-77.04,10.9,s_america",
            "Bangkok,13.75,100.50,10.7,se_asia",
            "Chennai,13.08,80.27,10.9,s_asia",
            "Hyderabad,17.39,78.49,9.9,s_asia",
            "Tehran,35.69,51.39,9.5,middle_east",
            "Seoul,37.57,127.00,9.6,e_asia",
            "Chengdu,30.66,104.07,9.1,e_asia",
            "Nanjing,32.06,118.78,9.0,e_asia",
            "Wuhan,30.59,114.31,9.4,e_asia",
            "Ho Chi Minh,10.82,106.63,9.0,se_asia",
            "London,51.51,-0.13,9.5,w_europe",
            "Ahmedabad,23.02,72.57,8.5,s_asia",
            "Xian,34.27,108.95,8.9,e_asia",
            "Baghdad,33.34,44.40,8.1,middle_east",
            "Paris,48.85,2.35,11.1,w_europe",
            "Chicago,41.85,-87.65,8.9,n_america",
            "Riyadh,24.69,46.72,7.7,middle_east",
            "Singapore,1.35,103.82,6.0,se_asia",
            "Toronto,43.70,-79.42,6.3,n_america",
            "Johannesburg,-26.20,28.04,5.6,s_africa",
            "Sydney,-33.87,151.21,5.3,oceania",
            "Nairobi,-1.29,36.82,5.1,s_africa"
        };

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("{\"cities\":[");
        for (int i = 0; i < rows.Length; i++)
        {
            var f = rows[i].Split(',');
            sb.Append($"  {{\"name\":\"{f[0]}\",\"lat\":{f[1]},\"lon\":{f[2]},\"populationM\":{f[3]},\"regionId\":\"{f[4]}\"}}");
            if (i < rows.Length - 1) sb.AppendLine(",");
        }
        sb.AppendLine("\n]}");
        W("Assets/Resources/Cities/cities.json", sb.ToString());
    }

    static void WriteLaunchSites()
    {
        var rows = new string[]
        {
            "Cape Canaveral,28.6,-80.6,north_america,rocket|icbm",
            "Vandenberg SFB,34.7,-120.6,north_america,rocket|icbm",
            "Baikonur,45.9,63.3,c_asia,rocket|icbm",
            "Plesetsk,62.9,40.7,russia,icbm|rocket",
            "Jiuquan,40.96,100.29,e_asia,rocket|icbm",
            "Xichang,28.25,102.03,e_asia,rocket",
            "Wenchang,19.61,110.95,e_asia,rocket",
            "Taiyuan,37.46,112.45,e_asia,rocket",
            "Satish Dhawan,13.72,80.23,s_asia,rocket",
            "Tanegashima,30.40,130.97,e_asia,rocket",
            "Kourou,5.24,-52.77,s_america,rocket",
            "Palmachim,31.90,34.69,middle_east,icbm|rocket",
            "Kapustin Yar,48.52,45.80,russia,icbm",
            "Woomera,-31.13,136.82,oceania,rocket",
            "Mahia,-39.26,177.86,oceania,rocket",
            "Esrange,67.89,21.07,w_europe,rocket"
        };

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("{\"sites\":[");
        for (int i = 0; i < rows.Length; i++)
        {
            var f = rows[i].Split(',');
            string types = "[\"" + f[4].Replace("|", "\",\"") + "\"]";
            sb.Append($"  {{\"name\":\"{f[0]}\",\"lat\":{f[1]},\"lon\":{f[2]},\"regionId\":\"{f[3]}\",\"types\":{types}}}");
            if (i < rows.Length - 1) sb.AppendLine(",");
        }
        sb.AppendLine("\n]}");
        W("Assets/Resources/LaunchSites/sites.json", sb.ToString());
    }
}
