using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Windows.Forms;
using System.Drawing;
using System.Xml.Serialization;
using System.Text.RegularExpressions;

public class GameCompatibility {
 public bool Supported;
 public string ActualBuild, TestedBuild;
 public string Warning {get{return "Перевод ещё не проверен для установленной сборки игры ("+ActualBuild+").\nПроверенная сборка: "+TestedBuild+".\n\nНекоторые тексты могут остаться на английском или устареть. Возможны ошибки отображения и работы игры.";}}
 public string Summary {get{return Supported?"Совместимость подтверждена.\nПроверенная сборка: "+TestedBuild:"Совместимость с этой сборкой не подтверждена ("+ActualBuild+").\nПеревод проверен для: "+TestedBuild+".\nНекоторые тексты могут остаться английскими или устареть.";}}
}
public class UpdateCheck {
 public GameCompatibility Compatibility;
 public byte[] PendingXml;
 public string Version;
 public bool Newer, Installed;
}

public class PatchFile { public string Relative; public string Hash; }
public class Receipt { public string Root; public string PatchVersion; public byte[] UpdateXml; public List<PatchFile> Files = new List<PatchFile>(); }
public static class Patch {
 public static TranslationUpdate Current;
 static byte[] CurrentXml;
 public static string TranslationVersion {get{return Current.Version;}}
 public static void Initialize(){CurrentXml=Resource("translation.xml");Current=TranslationUpdate.Parse(CurrentXml);}
 public static bool Installed(string root){return File.Exists(Under(root,Marker));}
 public static void UseInstalled(string root){
  if(!Installed(root))return;
  var r=ReadReceipt(Under(root,Marker));if(r.UpdateXml==null)return;
  var saved=TranslationUpdate.Parse(r.UpdateXml);
  if(System.Version.Parse(saved.Version)>System.Version.Parse(Current.Version)){Current=saved;CurrentXml=r.UpdateXml;}
 }
 public static bool SelectUpdate(string root,byte[] xml,bool allowUnverified=false){
  var next=TranslationUpdate.Parse(xml);
  if(System.Version.Parse(next.Version)<=System.Version.Parse(Current.Version))return false;
  ValidateGameWith(root,next,allowUnverified);Current=next;CurrentXml=xml;return true;
 }
 // Automatic updates never override the compatibility check or ask for consent.
 // An unverified offer remains separate until the user explicitly installs it.
 public static UpdateCheck CheckDownloadedUpdate(string root,byte[] xml){
  UseInstalled(root);
  var next=TranslationUpdate.Parse(xml);
  bool newer=System.Version.Parse(next.Version)>System.Version.Parse(Current.Version);
  var offered=newer?next:Current;
  var result=new UpdateCheck{Version=offered.Version,Newer=newer,Installed=Installed(root),Compatibility=InspectGame(root,offered)};
  if(!result.Compatibility.Supported){if(newer)result.PendingXml=xml;return result;}
  if(newer)SelectUpdate(root,xml);
  if(result.Installed)Install(root,false);
  return result;
 }
 const string Marker = "AnvilRussianPatch\\receipt.xml";
 const string Legacy = "Anvil\\Content\\Paks\\Anvil_Russian_Probe_P.pak";
 static readonly string[] LegacyHashes = {"4dc40d19652c3b4adbd8b65ec8c1c31f13cf2ee0cbe99dfb904fce841556c9ac", "776a1ab07deb0de065d2c525a8093378cd5f7f6fe84f2a66c9fc5bc0bcaaf90c"};
 public static string Hash(byte[] b) { using(var h=SHA256.Create()) return BitConverter.ToString(h.ComputeHash(b)).Replace("-", "").ToLowerInvariant(); }
 static string FileHash(string p) { using(var f=File.OpenRead(p)) using(var h=SHA256.Create()) return BitConverter.ToString(h.ComputeHash(f)).Replace("-", "").ToLowerInvariant(); }
 static byte[] Resource(string name) { using(var s=Assembly.GetExecutingAssembly().GetManifestResourceStream(name)) { if(s==null) throw new Exception("Не найден ресурс: "+name); using(var m=new MemoryStream()) {s.CopyTo(m);return m.ToArray();} } }
 static string FullRoot(string root) { return Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar); }
 static string Under(string root,string rel) { root=FullRoot(root); string p=Path.GetFullPath(Path.Combine(root,rel)); if(!p.StartsWith(root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)) throw new Exception("Недопустимый путь."); return p; }
 static Dictionary<string,byte[]> Payload() { return new Dictionary<string,byte[]> {
  {"Anvil\\Content\\Localization\\Game\\en\\Game.locres", Current.Files["Game.locres"]},
  {"Anvil\\Content\\Localization\\Game\\ru\\Game.locres", Current.Files["Game.locres"]},
  {"Anvil\\Content\\Localization\\Game\\Game.locmeta", Current.Files["Game.locmeta"]},
  {"Anvil\\Content\\Paks\\Anvil_Russian_Fonts_P.pak", Current.Files["Fonts.pak"]}
 }; }
 public static void RequireClosed() {
  if(Process.GetProcessesByName("Anvil-Win64-Shipping").Length>0 || Process.GetProcessesByName("Anvil").Length>0)
   throw new Exception("Закройте Anvil Empires перед установкой или удалением перевода.");
 }
 public static void ValidateGame(string root) {
  ValidateGameWith(root,Current);
 }
 public static GameCompatibility InspectGame(string root,TranslationUpdate update) {
  string exe=Under(root,"Anvil\\Binaries\\Win64\\Anvil-Win64-Shipping.exe");
  string pak=Under(root,"Anvil\\Content\\Paks\\Anvil-Windows.pak");
  if(!File.Exists(exe)||!File.Exists(pak))throw new Exception("Выберите корневую папку Anvil Playtest.");
  long pakSize=new FileInfo(pak).Length;
  if(pakSize<512||new FileInfo(exe).Length==0)throw new Exception("Файлы игры неполные. Дождитесь окончания загрузки или проверьте файлы через Steam.");
  byte[] tail=new byte[512];using(var f=File.OpenRead(pak)){f.Seek(-512,SeekOrigin.End);if(f.Read(tail,0,512)!=512)throw new IOException();}
  return new GameCompatibility{Supported=pakSize==update.PakSize&&Hash(tail)==update.TailHash&&FileHash(exe)==update.ExeHash,ActualBuild=SteamBuild(root),TestedBuild=update.GameVersion};
 }
 static string SteamBuild(string root){
  try{
   var common=Directory.GetParent(FullRoot(root));
   if(common==null||!string.Equals(common.Name,"common",StringComparison.OrdinalIgnoreCase)||common.Parent==null)return "номер не определён";
   string manifest=Path.Combine(common.Parent.FullName,"appmanifest_2383950.acf");
   var match=Regex.Match(File.ReadAllText(manifest),"\"buildid\"\\s+\"([0-9]+)\"");
   if(match.Success&&match.Groups[1].Value!="0")return "Steam "+match.Groups[1].Value;
  }catch{}
  return "номер не определён";
 }
 public static void ValidateGameWith(string root,TranslationUpdate update,bool allowUnverified=false) {
  var compatibility=InspectGame(root,update);
  if(!compatibility.Supported&&!allowUnverified)throw new Exception(compatibility.Warning+"\n\nУстановка возможна после подтверждения предупреждения в патчере. Файлы не изменены.");
 }
 static Receipt ReadReceipt(string path) {using(var f=File.OpenRead(path))return (Receipt)new XmlSerializer(typeof(Receipt)).Deserialize(f);}
 static void SaveReceipt(string path,Receipt r) {Directory.CreateDirectory(Path.GetDirectoryName(path));using(var f=new FileStream(path,FileMode.CreateNew))new XmlSerializer(typeof(Receipt)).Serialize(f,r);}
 static Receipt ValidReceipt(string root) {
  Receipt r=ReadReceipt(Under(root,Marker));
  if(!string.Equals(FullRoot(r.Root),FullRoot(root),StringComparison.OrdinalIgnoreCase))throw new Exception("Папка установки не совпадает с записью патчера.");
  var expected=Payload();var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
  foreach(var e in r.Files) if(!seen.Add(e.Relative)||!expected.ContainsKey(e.Relative)||!KnownHash(e.Relative,e.Hash,expected))throw new Exception("Запись установки повреждена. Автоматическое удаление отменено.");
  if(r.Files.Count!=expected.Count)throw new Exception("Неполная запись установки.");
  return r;
 }
 static bool KnownHash(string relative,string hash,Dictionary<string,byte[]> expected) {
  if(Hash(expected[relative])==hash)return true;
  return (relative=="Anvil\\Content\\Localization\\Game\\en\\Game.locres"||relative=="Anvil\\Content\\Localization\\Game\\ru\\Game.locres")&&Current.Previous.Contains(hash);
 }
 static void Upgrade(string root,Receipt old,Dictionary<string,byte[]> payload) {
  string marker=Under(root,Marker);byte[] oldMarker=File.ReadAllBytes(marker);var previous=new Dictionary<string,byte[]>();
  foreach(var e in old.Files)previous[e.Relative]=File.ReadAllBytes(Under(root,e.Relative));
  try{
   var next=new Receipt{Root=root,PatchVersion=Current.Version,UpdateXml=CurrentXml};foreach(var e in payload){File.WriteAllBytes(Under(root,e.Key),e.Value);next.Files.Add(new PatchFile{Relative=e.Key,Hash=Hash(e.Value)});}
   foreach(var e in next.Files)if(FileHash(Under(root,e.Relative))!=e.Hash)throw new IOException("Ошибка проверки обновления.");
   using(var f=new FileStream(marker,FileMode.Create))new XmlSerializer(typeof(Receipt)).Serialize(f,next);
  }catch{foreach(var e in previous)File.WriteAllBytes(Under(root,e.Key),e.Value);File.WriteAllBytes(marker,oldMarker);throw;}
 }
 public static void Install(string root,bool test,bool allowUnverified=false) {
  root=FullRoot(root);UseInstalled(root);if(!test){RequireClosed();ValidateGameWith(root,Current,allowUnverified);}
  var payload=Payload();string marker=Under(root,Marker);
  if(File.Exists(marker)){
   var old=ValidReceipt(root);
   foreach(var e in old.Files)if(!File.Exists(Under(root,e.Relative))||FileHash(Under(root,e.Relative))!=e.Hash)throw new Exception("Установка неполная или изменена. Сначала нажмите «Удалить перевод», затем установите снова.");
   bool needsUpdate=old.PatchVersion!=Current.Version;foreach(var e in old.Files)if(e.Hash!=Hash(payload[e.Relative]))needsUpdate=true;
   if(needsUpdate)Upgrade(root,old,payload);
   return;
  }
  foreach(var e in payload)if(File.Exists(Under(root,e.Key)))throw new Exception("Уже существует файл другой локализации: "+e.Key+". Он не будет перезаписан.");
  string legacy=Under(root,Legacy),backup=Under(root,"AnvilRussianPatch\\previous-probe.pak.off");
  if(File.Exists(legacy)){
   if(Array.IndexOf(LegacyHashes,FileHash(legacy))<0)throw new Exception("Найден неизвестный пробный пакет. Установка отменена без замены файлов.");
   if(File.Exists(backup))throw new Exception("Сохранён предыдущий пробный пакет. Сначала удалите текущий перевод через патчер.");
  }
  var receipt=new Receipt{Root=root,PatchVersion=Current.Version,UpdateXml=CurrentXml};foreach(var e in payload)receipt.Files.Add(new PatchFile{Relative=e.Key,Hash=Hash(e.Value)});
  SaveReceipt(marker,receipt);bool moved=false;
  try {
   if(File.Exists(legacy)){File.Move(legacy,backup);moved=true;}
   foreach(var e in payload){string path=Under(root,e.Key);Directory.CreateDirectory(Path.GetDirectoryName(path));using(var f=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None))f.Write(e.Value,0,e.Value.Length);}
   foreach(var e in receipt.Files)if(FileHash(Under(root,e.Relative))!=e.Hash)throw new IOException("Ошибка проверки записанных файлов.");
  } catch {
   foreach(var e in receipt.Files){string path=Under(root,e.Relative);if(File.Exists(path)&&FileHash(path)==e.Hash)File.Delete(path);}
   if(moved&&!File.Exists(legacy))File.Move(backup,legacy);
   bool left=false;foreach(var e in receipt.Files)if(File.Exists(Under(root,e.Relative)))left=true;
   if(!left)File.Delete(marker);
   throw;
  }
 }
 public static void Uninstall(string root,bool test) {
  root=FullRoot(root);UseInstalled(root);if(!test)RequireClosed();string marker=Under(root,Marker);
  if(!File.Exists(marker))throw new Exception("Запись установки не найдена. Файлы не изменены.");
  var r=ValidReceipt(root);
  foreach(var e in r.Files){string p=Under(root,e.Relative);if(File.Exists(p)&&FileHash(p)!=e.Hash)throw new Exception("Файл изменён после установки: "+e.Relative+". Автоматическое удаление отменено.");}
  foreach(var e in r.Files){string p=Under(root,e.Relative);if(File.Exists(p))File.Delete(p);}
  // The obsolete probe is retained as an inert .off backup; do not reactivate it.
  string backup=Under(root,"AnvilRussianPatch\\previous-probe.pak.off");
  if(File.Exists(backup)&&Array.IndexOf(LegacyHashes,FileHash(backup))>=0)File.Delete(backup);
  File.Delete(marker);
 }
 public static void SelfTest(string directory) {
  string root=Under(directory,"fixture-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
  string report=Under(directory,"patcher-selftest.txt");var checks=new List<string>();
  try {
   bool rejected=false;try{ValidateGame(root);}catch{rejected=true;}if(!rejected)throw new Exception("Missing game accepted");checks.Add("Missing game rejected.");
   string unrelated=Under(root,"keep.txt");File.WriteAllText(unrelated,"untouched");
   Install(root,true);foreach(var e in Payload())if(FileHash(Under(root,e.Key))!=Hash(e.Value))throw new Exception("Install hash mismatch");checks.Add("All payloads installed and hash-verified.");
   Install(root,true);checks.Add("Repeated installation is idempotent.");
   var prior=ValidReceipt(root);foreach(var e in prior.Files)if(e.Relative.EndsWith(".locres")){byte[] v1=Resource("Previous.locres");File.WriteAllBytes(Under(root,e.Relative),v1);e.Hash=Hash(v1);}
   string priorMarker=Under(root,Marker);File.Delete(priorMarker);SaveReceipt(priorMarker,prior);Install(root,true);
   foreach(var e in Payload())if(FileHash(Under(root,e.Key))!=Hash(e.Value))throw new Exception("Upgrade mismatch");checks.Add("Previous version upgrades successfully.");
   string changed=Under(root,"Anvil\\Content\\Localization\\Game\\en\\Game.locres");byte[] original=File.ReadAllBytes(changed);File.AppendAllText(changed,"changed");
   rejected=false;try{Uninstall(root,true);}catch{rejected=true;}if(!rejected||!File.Exists(changed))throw new Exception("Changed file was not protected");checks.Add("Modified files protected from removal.");File.WriteAllBytes(changed,original);
   Uninstall(root,true);foreach(var e in Payload())if(File.Exists(Under(root,e.Key)))throw new Exception("Uninstall incomplete");if(File.ReadAllText(unrelated)!="untouched")throw new Exception("Unrelated file changed");checks.Add("Uninstall removed only managed files.");
   Directory.CreateDirectory(Path.GetDirectoryName(changed));File.WriteAllText(changed,"other translator");rejected=false;try{Install(root,true);}catch{rejected=true;}if(!rejected||File.ReadAllText(changed)!="other translator")throw new Exception("Collision not protected");checks.Add("Existing localization protected from overwrite.");File.Delete(changed);
   checks.Add("PASS");File.WriteAllLines(report,checks.ToArray(),Encoding.UTF8);
  }catch(Exception e){checks.Add("FAIL: "+e);File.WriteAllLines(report,checks.ToArray(),Encoding.UTF8);throw;}
 }
}
public class MainForm:Form {
 TextBox path=new TextBox();Label status=new Label(),compatibility=new Label();Button install=new Button(),remove=new Button(),check=new Button();bool busy;byte[] pendingUpdate;
 public MainForm(){Text="Anvil Russian — RikiMarci";ClientSize=new Size(640,610);FormBorderStyle=FormBorderStyle.FixedDialog;MaximizeBox=false;Font=new Font("Segoe UI",10);StartPosition=FormStartPosition.CenterScreen;
  Controls.Add(new Label{Text="Русский интерфейс Anvil Empires",Font=new Font("Segoe UI",16,FontStyle.Bold),Left=22,Top=20,Width=590,Height=38});
  Controls.Add(new Label{Text="Перевод: RikiMarci · Выпуск "+Patch.TranslationVersion+"\nОбновления проверяются при запуске патчера.\nПеред установкой или удалением закройте игру.",Left=24,Top=66,Width=590,Height=64});
  compatibility.Text="Выберите папку игры для проверки совместимости.";compatibility.SetBounds(24,140,590,90);Controls.Add(compatibility);
  Controls.Add(new Label{Text="Папка игры:",Left=24,Top=235,Width=580,Height=24});path.SetBounds(24,264,485,28);path.Text=@"D:\SteamLibrary\steamapps\common\Anvil Playtest";
  try{if(File.Exists(SettingsFile))path.Text=File.ReadAllText(SettingsFile);}catch{}Controls.Add(path);
  var browse=new Button{Text="Обзор…",Left=522,Top=262,Width=94,Height=32};browse.Click+=async(s,e)=>{if(busy)return;using(var d=new FolderBrowserDialog()){d.Description="Папка Anvil Playtest";d.SelectedPath=path.Text;if(d.ShowDialog()==DialogResult.OK){path.Text=d.SelectedPath;await RefreshCompatibility(path.Text);}}};Controls.Add(browse);
  path.TextChanged+=(s,e)=>{compatibility.ForeColor=SystemColors.ControlText;compatibility.Text="Папка изменена. Совместимость будет проверена перед установкой.";};
  install.Text="Установить / обновить";install.SetBounds(24,310,282,40);install.Click+=async(s,e)=>await Run(true);Controls.Add(install);
  remove.Text="Удалить перевод";remove.SetBounds(320,310,296,40);remove.Click+=async(s,e)=>await Run(false);Controls.Add(remove);
  check.Text="Проверить GitHub";check.SetBounds(24,362,282,36);check.Click+=async(s,e)=>await Check();Controls.Add(check);
  var releases=new LinkLabel{Text="Все выпуски на GitHub",Left=330,Top=368,Width=270,Height=26};releases.LinkClicked+=(s,e)=>Open(Updater.Repository+"/releases");Controls.Add(releases);
  status.Text="Готово к проверке обновлений.";status.SetBounds(24,415,590,128);Controls.Add(status);
  var discord=new LinkLabel{Text="Discord RikiMarci — discord.gg/VgK7Pw3cwf",Left=24,Top=570,Width=590,Height=26};discord.LinkClicked+=(s,e)=>Open("https://discord.gg/VgK7Pw3cwf");Controls.Add(discord);
  Shown+=async(s,e)=>await Check();
 }
 static string SettingsFile {get{return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"AnvilRussian","game-path.txt");}}
 void SavePath(){try{Directory.CreateDirectory(Path.GetDirectoryName(SettingsFile));File.WriteAllText(SettingsFile,path.Text);}catch{}}
 void SetBusy(bool value){busy=value;install.Enabled=remove.Enabled=check.Enabled=path.Enabled=!value;}
 void Open(string url){try{Process.Start(new ProcessStartInfo(url){UseShellExecute=true});}catch(Exception ex){status.Text=ex.Message;}}
 TranslationUpdate OfferedUpdate(){
  if(pendingUpdate!=null){var next=TranslationUpdate.Parse(pendingUpdate);if(System.Version.Parse(next.Version)>System.Version.Parse(Patch.TranslationVersion))return next;}
  return Patch.Current;
 }
 async System.Threading.Tasks.Task RefreshCompatibility(string root){
  try{
   var offered=OfferedUpdate();var result=await System.Threading.Tasks.Task.Run(()=>Patch.InspectGame(root,offered));
   if(path.Text!=root)return;
   compatibility.ForeColor=result.Supported?Color.DarkGreen:Color.Maroon;compatibility.Text=result.Summary;
  }catch(Exception ex){if(path.Text==root){compatibility.ForeColor=SystemColors.ControlText;compatibility.Text=ex.Message;}}
 }
 async System.Threading.Tasks.Task Check(){
  if(busy)return;SetBusy(true);string root=path.Text;status.Text="Проверяю GitHub…";pendingUpdate=null;
  try{
  try{
   await System.Threading.Tasks.Task.Run(()=>Patch.UseInstalled(root));
   byte[] data=await System.Threading.Tasks.Task.Run(()=>Updater.Download());
   // Keep the downloaded offer even if the selected game folder is not valid yet.
   if(System.Version.Parse(TranslationUpdate.Parse(data).Version)>System.Version.Parse(Patch.TranslationVersion))pendingUpdate=data;
   var result=await System.Threading.Tasks.Task.Run(()=>Patch.CheckDownloadedUpdate(root,data));pendingUpdate=result.PendingXml;
   if(!result.Compatibility.Supported){
    status.Text="Доступен перевод "+result.Version+", но эта сборка игры не проверена.\nДля установки с предупреждением нажмите «Установить / обновить».\nПроверка новых выпусков продолжит работать.";
   }else status.Text=result.Installed?"Установлена актуальная версия перевода: "+result.Version+".\nМожно запускать игру через Steam.":"Доступен перевод: "+result.Version+".\nНажмите «Установить / обновить».";
   SavePath();
  }catch(Exception ex){status.Text="Автообновление не выполнено: "+ex.Message+"\nСохранённый перевод доступен через кнопку установки.";}
  await RefreshCompatibility(root);
  }finally{SetBusy(false);}
 }
 async System.Threading.Tasks.Task Run(bool add){
  if(busy)return;SetBusy(true);string root=path.Text;status.Text="Проверка файлов…";
  try{
  try{
   if(!add){await System.Threading.Tasks.Task.Run(()=>Patch.Uninstall(root,false));status.Text="Перевод удалён.";}
   else{
    await System.Threading.Tasks.Task.Run(()=>{Patch.RequireClosed();Patch.UseInstalled(root);});
    var offered=OfferedUpdate();var info=await System.Threading.Tasks.Task.Run(()=>Patch.InspectGame(root,offered));
    bool allowUnverified=false;
    if(!info.Supported){
     allowUnverified=MessageBox.Show(this,info.Warning+"\n\nУстановить перевод "+offered.Version+" всё равно?","Установка на непроверенную сборку",MessageBoxButtons.YesNo,MessageBoxIcon.Warning,MessageBoxDefaultButton.Button2)==DialogResult.Yes;
     if(!allowUnverified){status.Text="Установка отменена. Файлы не изменены.";await RefreshCompatibility(root);return;}
    }
    byte[] candidate=pendingUpdate;
    await System.Threading.Tasks.Task.Run(()=>{if(candidate!=null)Patch.SelectUpdate(root,candidate,allowUnverified);Patch.Install(root,false,allowUnverified);});
    pendingUpdate=null;
    status.Text="Перевод "+Patch.TranslationVersion+" установлен."+(allowUnverified?"\nСовместимость с этой сборкой не подтверждена. Некоторые тексты могут быть английскими или устаревшими.":"\nЗапустите игру через Steam.");
   }
   SavePath();
  }catch(Exception ex){status.Text=ex.Message;}
  await RefreshCompatibility(root);
  }finally{SetBusy(false);}
 }
 [STAThread]public static int Main(string[] args){try{
  Patch.Initialize();
  if(args.Length==2&&args[0]=="--self-test"){Patch.SelfTest(args[1]);UpdateTests.Run(args[1]);CompatibilityTests.Run(args[1]);return 0;}
  if(args.Length==2&&args[0]=="--verify-game"){Patch.ValidateGame(args[1]);return 0;}
  if(args.Length==2&&args[0]=="--install"){Patch.Install(args[1],false);return 0;}
  if(args.Length==2&&args[0]=="--install-unverified"){Patch.UseInstalled(args[1]);var info=Patch.InspectGame(args[1],Patch.Current);if(!info.Supported)Console.Error.WriteLine(info.Warning);Patch.Install(args[1],false,true);return 0;}
  if(args.Length==2&&args[0]=="--check-feed"){var data=Updater.Download();var u=TranslationUpdate.Parse(data);Patch.ValidateGameWith(args[1],u);Console.WriteLine(u.Version);return 0;}
  Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);Application.Run(new MainForm());return 0;
 }catch(Exception e){if(args.Length>0){Console.Error.WriteLine(e);return 1;}MessageBox.Show(e.Message);return 1;}}
}
