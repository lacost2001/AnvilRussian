using System;
using System.IO;
using System.Net;
using System.Xml;
using System.Collections.Generic;
using System.Text.RegularExpressions;

public class TranslationUpdate {
 public string Version, GameVersion, ExeHash, TailHash;
 public long PakSize;
 public Dictionary<string,byte[]> Files=new Dictionary<string,byte[]>();
 public HashSet<string> Previous=new HashSet<string>();
 public static TranslationUpdate Parse(byte[] bytes) {
  if(bytes.Length>16*1024*1024)throw new Exception("Слишком большой пакет обновления.");
  var settings=new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=16*1024*1024};
  var doc=new XmlDocument{XmlResolver=null};using(var m=new MemoryStream(bytes))using(var r=XmlReader.Create(m,settings))doc.Load(r);
  var e=doc.DocumentElement;if(e.Name!="TranslationUpdate"||e.GetAttribute("schema")!="1")throw new Exception("Неподдерживаемый формат обновления.");
  var u=new TranslationUpdate{Version=e.GetAttribute("version"),GameVersion=e.GetAttribute("gameVersion"),ExeHash=e.GetAttribute("exeHash"),TailHash=e.GetAttribute("pakTailHash")};
  System.Version.Parse(u.Version);
  if(!Digest(u.ExeHash)||!Digest(u.TailHash)||!long.TryParse(e.GetAttribute("pakSize"),out u.PakSize)||u.PakSize<512)throw new Exception("Некорректная версия игры в обновлении.");
  foreach(XmlNode n in e.ChildNodes){
   if(n is XmlComment)continue;var f=n as XmlElement;if(f==null)continue;
   if(f.Name=="PreviousLocres"){if(!Digest(f.GetAttribute("sha256")))throw new Exception("Некорректный хеш.");u.Previous.Add(f.GetAttribute("sha256"));continue;}
   string name=f.GetAttribute("name");
   if(f.Name!="File"||(name!="Game.locres"&&name!="Game.locmeta"&&name!="Fonts.pak")||u.Files.ContainsKey(name))throw new Exception("Недопустимое содержимое обновления.");
   byte[] data=Convert.FromBase64String(f.InnerText);
   if(data.Length==0||data.Length>8*1024*1024||Patch.Hash(data)!=f.GetAttribute("sha256"))throw new Exception("Файл обновления повреждён.");
   u.Files.Add(name,data);
  }
  if(u.Files.Count!=3)throw new Exception("Неполный пакет перевода.");
  return u;
 }
 static bool Digest(string s){return Regex.IsMatch(s??"","\u005e[a-f0-9]{64}$");}
}
public static class Updater {
 public const string Repository="https://github.com/lacost2001/AnvilRussian";
 public const string Feed=Repository+"/releases/latest/download/translation.xml";
 public static byte[] Download() {
  ServicePointManager.SecurityProtocol=SecurityProtocolType.Tls12;
  string url=Feed;
  for(int redirects=0;redirects<6;redirects++){
   var uri=new Uri(url);
   if(uri.Scheme!="https"||(uri.Host!="github.com"&&uri.Host!="release-assets.githubusercontent.com"&&uri.Host!="objects.githubusercontent.com"))throw new Exception("Недопустимый адрес обновления.");
   var request=(HttpWebRequest)WebRequest.Create(uri);request.UserAgent="AnvilRussian-Updater/1.2";request.Timeout=20000;request.ReadWriteTimeout=20000;request.AllowAutoRedirect=false;
   using(var response=(HttpWebResponse)request.GetResponse()){
    int code=(int)response.StatusCode;if(code>=300&&code<400){url=new Uri(uri,response.Headers["Location"]).AbsoluteUri;continue;}
    if(response.ContentLength>16*1024*1024)throw new Exception("Слишком большой пакет.");
    using(var input=response.GetResponseStream())using(var m=new MemoryStream()){
     var buffer=new byte[32768];int n;while((n=input.Read(buffer,0,buffer.Length))>0){if(m.Length+n>16*1024*1024)throw new Exception("Слишком большой пакет.");m.Write(buffer,0,n);}return m.ToArray();
    }
   }
  }throw new Exception("Слишком много перенаправлений.");
 }
}
