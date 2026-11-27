using System;
using System.IO;
using System.Text;
using System.Xml;
using System.Reflection;

public static class CompatibilityTests {
 static void Require(bool value,string message){if(!value)throw new Exception(message);}
 static void Reject(Action action,string message){bool rejected=false;try{action();}catch{rejected=true;}Require(rejected,message);}
 static string Snapshot(string root){
  string[] paths=Directory.GetFiles(root,"*",SearchOption.AllDirectories);Array.Sort(paths,StringComparer.Ordinal);
  var result=new StringBuilder();foreach(string path in paths)result.Append(path).Append(':').Append(Patch.Hash(File.ReadAllBytes(path))).Append('\n');return result.ToString();
 }
 static byte[] Feed(byte[] source,string version,byte[] exe=null,byte[] pak=null){
  var doc=new XmlDocument();doc.LoadXml(Encoding.UTF8.GetString(source));doc.DocumentElement.SetAttribute("version",version);
  if(exe!=null){doc.DocumentElement.SetAttribute("exeHash",Patch.Hash(exe));doc.DocumentElement.SetAttribute("pakTailHash",Patch.Hash(pak));doc.DocumentElement.SetAttribute("pakSize",pak.Length.ToString());}
  return Encoding.UTF8.GetBytes(doc.OuterXml);
 }
 public static void Run(string directory){
  byte[] raw;using(var s=Assembly.GetExecutingAssembly().GetManifestResourceStream("translation.xml"))using(var m=new MemoryStream()){s.CopyTo(m);raw=m.ToArray();}
  string version=TranslationUpdate.Parse(raw).Version;
  string fixture=Path.Combine(directory,"compatibility-fixture-"+Guid.NewGuid().ToString("N"));
  string root=Path.Combine(fixture,"steamapps\\common\\Anvil Playtest");Directory.CreateDirectory(root);
  string exe=Path.Combine(root,"Anvil\\Binaries\\Win64\\Anvil-Win64-Shipping.exe"),pak=Path.Combine(root,"Anvil\\Content\\Paks\\Anvil-Windows.pak");
  string marker=Path.Combine(root,"AnvilRussianPatch\\receipt.xml"),locres=Path.Combine(root,"Anvil\\Content\\Localization\\Game\\en\\Game.locres");
  try{
   Patch.Initialize();
   Reject(()=>Patch.Install(root,false,true),"Override accepted missing game");Require(Snapshot(root)=="","Missing game installation wrote files");
   Directory.CreateDirectory(Path.GetDirectoryName(exe));Directory.CreateDirectory(Path.GetDirectoryName(pak));
   byte[] stub=Encoding.UTF8.GetBytes("game fixture, not executable"),tail=new byte[512];
   File.WriteAllBytes(exe,stub);File.WriteAllBytes(pak,new byte[20]);
   Reject(()=>Patch.Install(root,false,true),"Override accepted truncated archive");Require(!File.Exists(marker),"Truncated game wrote receipt");
   File.WriteAllBytes(pak,tail);
   File.WriteAllText(Path.Combine(fixture,"steamapps\\appmanifest_2383950.acf"),"\"AppState\" { \"buildid\" \"12345678\" }");
   var unknown=Patch.InspectGame(root,Patch.Current);Require(!unknown.Supported&&unknown.ActualBuild=="Steam 12345678","Unverified game inspection failed");
   Require(unknown.Warning.Contains(Patch.Current.GameVersion)&&unknown.Warning.Contains("английском")&&unknown.Warning.Contains("устареть"),"Warning omitted compatibility details");
   string before=Snapshot(root);
   Reject(()=>Patch.Install(root,false),"Default installation accepted unverified game");
   byte[] offer=Feed(raw,"98.0.0");
   Reject(()=>Patch.SelectUpdate(root,offer),"Default selection accepted unverified game");
   var check=Patch.CheckDownloadedUpdate(root,offer);
   Require(!check.Compatibility.Supported&&check.PendingXml!=null&&!check.Installed,"Unverified first install offer was lost");
   Require(Patch.TranslationVersion==version&&Snapshot(root)==before,"Unconfirmed offer changed active payload or files");

   Patch.Install(root,false,true);Require(File.Exists(marker)&&File.Exists(locres),"Explicit installation failed");
   Require(Patch.Hash(File.ReadAllBytes(exe))==Patch.Hash(stub)&&Patch.Hash(File.ReadAllBytes(pak))==Patch.Hash(tail),"Game files changed");
   Patch.Initialize();Patch.UseInstalled(root);before=Snapshot(root);
   Reject(()=>Patch.Install(root,false),"Previous consent silently enabled another installation");
   check=Patch.CheckDownloadedUpdate(root,offer);
   Require(check.Installed&&!check.Compatibility.Supported&&check.PendingXml!=null,"Unverified installed update was not deferred");
   Require(Patch.TranslationVersion==version&&Snapshot(root)==before,"Auto-update overwrote unverified installation");

   byte[] original=File.ReadAllBytes(locres);File.AppendAllText(locres,"user change");before=Snapshot(root);
   Reject(()=>Patch.Install(root,false,true),"Override overwrote user-modified translation");
   Require(Snapshot(root)==before,"Modified translation changed despite refusal");File.WriteAllBytes(locres,original);

   var corrupt=new XmlDocument();corrupt.LoadXml(Encoding.UTF8.GetString(offer));((XmlElement)corrupt.SelectSingleNode("/TranslationUpdate/File")).SetAttribute("sha256",new string('0',64));
   Reject(()=>Patch.SelectUpdate(root,Encoding.UTF8.GetBytes(corrupt.OuterXml),true),"Override accepted corrupt downloaded package");
   Require(Patch.TranslationVersion==version,"Corrupt package changed active version");
   Require(Patch.SelectUpdate(root,offer,true),"Explicit unverified update was not selected");Patch.Install(root,false,true);
   Patch.Initialize();Patch.UseInstalled(root);Require(Patch.TranslationVersion=="98.0.0"&&!Patch.InspectGame(root,Patch.Current).Supported,"Offline unverified update lost its warning");

   byte[] supported=Feed(raw,"99.0.0",stub,tail);
   check=Patch.CheckDownloadedUpdate(root,supported);
   Require(check.Compatibility.Supported&&check.PendingXml==null&&Patch.TranslationVersion=="99.0.0","Compatible update did not replace unverified package automatically");
   Patch.Initialize();Patch.UseInstalled(root);Require(Patch.TranslationVersion=="99.0.0"&&Patch.InspectGame(root,Patch.Current).Supported,"Compatible update was not persisted");
   tail[0]=1;File.WriteAllBytes(pak,tail);before=Snapshot(root);
   check=Patch.CheckDownloadedUpdate(root,supported);
   Require(!check.Compatibility.Supported&&Snapshot(root)==before,"New game change silently reused compatibility approval");
   Require(!Patch.SelectUpdate(root,raw,true),"Override allowed a translation downgrade");
   Patch.Uninstall(root,false);Require(!File.Exists(marker)&&!File.Exists(locres),"Unverified installation could not be removed");
   Require(Patch.Hash(File.ReadAllBytes(exe))==Patch.Hash(stub)&&Patch.Hash(File.ReadAllBytes(pak))==Patch.Hash(tail),"Removal changed main game files");

   Patch.Initialize();Directory.CreateDirectory(Path.GetDirectoryName(locres));File.WriteAllText(locres,"other localization");before=Snapshot(root);
   Reject(()=>Patch.Install(root,false,true),"Override replaced another localization");Require(Snapshot(root)==before,"Foreign localization was altered");
   File.WriteAllText(Path.Combine(directory,"compatibility-selftest.txt"),"PASS: unverified builds inspected and warned; defaults and automatic updates never bypass compatibility; explicit install/update works; missing/truncated games, corrupt packages and modified/foreign translations remain protected; offline warnings persist; compatible update resumes automatically; changed game requires new confirmation; downgrade and removal verified.");
  }finally{Patch.Initialize();}
 }
}
