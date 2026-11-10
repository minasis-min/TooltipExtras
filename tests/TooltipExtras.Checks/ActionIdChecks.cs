using System.Text;
using TooltipExtras;
namespace TooltipExtrasChecks;
internal static class ActionIdChecks
{
 public static void Run(Action<bool,string> check)
 {
  byte[] Full(string category,string id,bool newline=false)=>new Lumina.Text.SeStringBuilder().Append(category+(category.Length==0?"":newline?"\n":" ")).PushColorType(3).Append(id).PopColorType().ToArray();
  foreach(var category in new[]{"ウェポンスキル","","Ability "})
  foreach(var id in new[]{"[123]","[0x7B]","[123 - 0x7B]","[12→123]","[12→0x7B]","[4294967295 - 0xFFFFFFFF]"})
  foreach(var newline in new[]{false,true}){
   var full=Full(category,id,newline);var ok=ActionIdText.Split(full,out var original,out var separated);
   check(ok&&Encoding.UTF8.GetString(original)==category&&Dalamud.Game.Text.SeStringHandling.SeString.Parse(separated).TextValue==id,"action ID exact suffix: "+category+id+newline);
  }
  foreach(var id in new[]{"[123 - 0x7C]","[4294967296]","[0x100000000]","[12→12]","[12→123 - 0x7B]","[123] extra","[1:23]","[0x7b]"})
   check(!ActionIdText.Split(Full("Ability",id),out _,out _),"reject unreviewed action suffix "+id);
  check(!ActionIdText.Split(Encoding.UTF8.GetBytes("Ability [123]"),out _,out _),"native bracketed number unchanged");
  var formatted=new Lumina.Text.SeStringBuilder().PushColorType(4).Append("Ability").PopColorType().Append(" ").PushColorType(3).Append("[123]").PopColorType().ToArray();
  check(!ActionIdText.Split(formatted,out _,out _),"foreign category formatting unchanged");
  var duplicate=new Lumina.Text.SeStringBuilder().PushColorType(3).Append("[1]").PopColorType().Append(" ").PushColorType(3).Append("[2]").PopColorType().ToArray();
  check(!ActionIdText.Split(duplicate,out _,out _),"duplicate suffix unchanged");
  foreach(var bytes in new[]{Array.Empty<byte>(),new byte[32769],new byte[]{2,19,2,3,3}})
   check(!ActionIdText.Split(bytes,out _,out _),"invalid bounded action input rejected");
 }
}
