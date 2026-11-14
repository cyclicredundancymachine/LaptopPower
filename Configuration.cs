using System;
using System.IO;
using System.Collections.Generic;
using System.Web.Script.Serialization;

public sealed class PowerValues {
    public int cpuMinimum { get; set; }
    public int cpuMaximum { get; set; }
    public int boostMode { get; set; }
    public int efficiencyPreference { get; set; }
    public string screenAction { get; set; }
    public int screenBrightness { get; set; }
    public int displayTimeoutMinutes { get; set; }
    public int sleepTimeoutMinutes { get; set; }
    public PowerValues() { cpuMinimum=cpuMaximum=boostMode=efficiencyPreference=screenBrightness=displayTimeoutMinutes=sleepTimeoutMinutes=-999; }
}
public sealed class PowerModeConfig {
    public string id { get; set; }
    public string name { get; set; }
    public PowerValues pluggedIn { get; set; }
    public PowerValues battery { get; set; }
    public int keyboardBrightness { get; set; }
    public int thermalMode { get; set; }
    public int nvidiaBattery { get; set; }
    public int nvidiaUsb { get; set; }
    public int nvidiaBarrel { get; set; }
    public PowerModeConfig() { keyboardBrightness=thermalMode=-999; }
}
public sealed class PowerConfig {
    public int version { get; set; }
    public PowerModeConfig[] modes { get; set; }
    static readonly string[] Ids={"save","everyday","full"};
    static PowerValues Values(int max,int boost,int epp,int brightness) {
        return new PowerValues {cpuMinimum=5,cpuMaximum=max,boostMode=boost,efficiencyPreference=epp,screenAction="Set",screenBrightness=brightness,displayTimeoutMinutes=-1,sleepTimeoutMinutes=-1};
    }
    public static PowerConfig Defaults() {
        return new PowerConfig {version=1,modes=new [] {
            new PowerModeConfig {id="save",name="Endurance",pluggedIn=Values(40,0,100,50),battery=Values(40,0,100,50),keyboardBrightness=-1,thermalMode=-1},
            new PowerModeConfig {id="everyday",name="Balanced",pluggedIn=Values(80,0,50,80),battery=Values(80,0,50,80),keyboardBrightness=-1,thermalMode=-1},
            new PowerModeConfig {id="full",name="Boost",pluggedIn=Values(100,2,0,100),battery=Values(100,2,0,100),keyboardBrightness=-1,thermalMode=-1}
        }};
    }
    static void Range(int value,int min,int max,string label) {
        if(value<min || value>max) throw new InvalidDataException(label+" must be between "+min+" and "+max+".");
    }
    static void ValidateValues(PowerValues p,string label) {
        if(p==null) throw new InvalidDataException(label+" is missing.");
        Range(p.cpuMinimum,0,100,label+" CPU minimum"); Range(p.cpuMaximum,1,100,label+" CPU maximum");
        if(p.cpuMinimum>p.cpuMaximum) throw new InvalidDataException(label+": CPU minimum cannot exceed maximum.");
        Range(p.boostMode,0,6,label+" CPU boost"); Range(p.efficiencyPreference,0,100,label+" efficiency preference");
        if(p.screenAction!="Set" && p.screenAction!="Keep" && p.screenAction!="Restore") throw new InvalidDataException(label+": screenAction must be Set, Keep or Restore.");
        Range(p.screenBrightness,1,100,label+" screen brightness");
        Range(p.displayTimeoutMinutes,-1,1440,label+" screen timeout"); Range(p.sleepTimeoutMinutes,-1,1440,label+" sleep timeout");
    }
    public void Validate() {
        if(version!=1 || modes==null || modes.Length!=3) throw new InvalidDataException("config.json must have version 1 and exactly three modes.");
        for(int i=0;i<3;i++) {
            var m=modes[i];
            if(m==null || m.id!=Ids[i]) throw new InvalidDataException("Modes must appear in this order: save, everyday, full.");
            if(String.IsNullOrWhiteSpace(m.name) || m.name.Length>45) throw new InvalidDataException("Each mode needs a name of 1 to 45 characters.");
            Range(m.keyboardBrightness,-1,3,m.name+" keyboard brightness"); Range(m.thermalMode,-1,2,m.name+" ASUS preset");
            Range(m.nvidiaBattery,0,2,m.name+" NVIDIA on battery");
            Range(m.nvidiaUsb,0,2,m.name+" NVIDIA on USB-C");
            Range(m.nvidiaBarrel,0,2,m.name+" NVIDIA on barrel charger");
            ValidateValues(m.pluggedIn,m.name+" / plugged in"); ValidateValues(m.battery,m.name+" / battery");
        }
    }
    public static PowerConfig Load(string path) {
        if(!File.Exists(path)) Save(path,Defaults());
        try {
            var config=new JavaScriptSerializer().Deserialize<PowerConfig>(File.ReadAllText(path));
            if(config==null) throw new InvalidDataException("Configuration is empty.");
            config.Validate(); return config;
        } catch(Exception e) {throw new InvalidDataException("Cannot use "+path+": "+e.Message,e);}
    }
    public static void Save(string path,PowerConfig config) {
        config.Validate();
        string json=new JavaScriptSerializer().Serialize(config);
        // Indent the JSON without rewriting content inside string values.
        var formatted=new System.Text.StringBuilder(); int level=0; bool quoted=false,escaped=false;
        foreach(char ch in json) {
            if(quoted) {formatted.Append(ch); if(escaped) escaped=false; else if(ch=='\\') escaped=true; else if(ch=='"') quoted=false; continue;}
            if(ch=='"') {quoted=true;formatted.Append(ch);}
            else if(ch=='{' || ch=='[') {formatted.Append(ch).AppendLine();level++;formatted.Append(' ',level*2);}
            else if(ch=='}' || ch==']') {level--;formatted.AppendLine().Append(' ',level*2).Append(ch);}
            else if(ch==',') {formatted.Append(ch).AppendLine().Append(' ',level*2);}
            else if(ch==':') formatted.Append(": "); else formatted.Append(ch);
        }
        string temporary=path+".tmp";
        File.WriteAllText(temporary,formatted.ToString(),new System.Text.UTF8Encoding(false));
        if(File.Exists(path)) File.Replace(temporary,path,path+".bak"); else File.Move(temporary,path);
    }
}
public sealed class SettingResult {
    public string setting { get; set; }
    public string requested { get; set; }
    public string actual { get; set; }
    public string status { get; set; }
    public string detail { get; set; }
}
public sealed class OperationReport {
    public string mode { get; set; }
    public string time { get; set; }
    public string summary { get; set; }
    public int targetModeIndex { get; set; }
    public SettingResult[] rows { get; set; }
}
