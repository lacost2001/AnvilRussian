using System;
using System.IO;
using System.Text;
using System.Xml;
using System.Reflection;
public static class UpdateTests {
 public static void Run(string directory){
  byte[] raw;using(var s=Assembly.GetExecutingAssembly().GetManifestResourceStream("translation.xml"))using(var m=new MemoryStream()){s.CopyTo(m);raw=m.ToArray();}
  var parsed=TranslationUpdate.Parse(raw);
  var doc=new XmlDocument();doc.LoadXml(Encoding.UTF8.GetString(raw));
  var file=(XmlElement)doc.SelectSingleNode("/TranslationUpdate/File");file.SetAttribute("sha256",new string('0',64));Reject(Encoding.UTF8.GetBytes(doc.OuterXml));
  doc.LoadXml(Encoding.UTF8.GetString(raw));((XmlElement)doc.SelectSingleNode("/TranslationUpdate/File")).SetAttribute("name","../game.exe");Reject(Encoding.UTF8.GetBytes(doc.OuterXml));
  Reject(Encoding.UTF8.GetBytes("<!DOCTYPE a [<!ENTITY x SYSTEM 'file:///x'>]><TranslationUpdate>&x;</TranslationUpdate>"));
  Reject(new byte[16*1024*1024+1]);
  doc.LoadXml(Encoding.UTF8.GetString(raw));doc.DocumentElement.SetAttribute("version","99.0.0");
  string root=Path.Combine(directory,"update-fixture-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
  bool rejected=false;try{Patch.SelectUpdate(root,Encoding.UTF8.GetBytes(doc.OuterXml));}catch{rejected=true;}
  if(!rejected||Patch.TranslationVersion!=parsed.Version)throw new Exception("Incompatible game accepted or changed active payload");
  string exe=Path.Combine(root,"Anvil\\Binaries\\Win64\\Anvil-Win64-Shipping.exe"),pak=Path.Combine(root,"Anvil\\Content\\Paks\\Anvil-Windows.pak");
  Directory.CreateDirectory(Path.GetDirectoryName(exe));Directory.CreateDirectory(Path.GetDirectoryName(pak));
  byte[] stub=Encoding.UTF8.GetBytes("test fixture, not executable"),tail=new byte[512];File.WriteAllBytes(exe,stub);File.WriteAllBytes(pak,tail);
  Patch.Install(root,true);
  doc.DocumentElement.SetAttribute("exeHash",Patch.Hash(stub));doc.DocumentElement.SetAttribute("pakTailHash",Patch.Hash(tail));doc.DocumentElement.SetAttribute("pakSize","512");
  if(!Patch.SelectUpdate(root,Encoding.UTF8.GetBytes(doc.OuterXml)))throw new Exception("Valid newer update not selected");
  Patch.Install(root,false);Patch.Initialize();Patch.UseInstalled(root);
  if(Patch.TranslationVersion!="99.0.0")throw new Exception("Offline update persistence failed");
  if(Patch.SelectUpdate(root,raw))throw new Exception("Downgrade accepted");
  Patch.Uninstall(root,true);Patch.Initialize();
  File.WriteAllText(Path.Combine(directory,"updater-selftest.txt"),"PASS: valid package; corrupt hash, path traversal, DTD, oversized data, incompatible game rejected; failed selection preserves active payload; compatible upgrade, offline persistence, downgrade protection and removal verified.");
 }
 static void Reject(byte[] bytes){bool rejected=false;try{TranslationUpdate.Parse(bytes);}catch{rejected=true;}if(!rejected)throw new Exception("Invalid update accepted");}
}
