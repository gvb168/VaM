using System;
using System.Collections.Generic;
using System.Text;
using SimpleJSON;
using UnityEngine;
using MVR.FileManagementSecure;

namespace GlebVAM
{
    // NativeFutanariHairAdapter v181
    // Owns the risky opposite-gender CustomHairItem resolution/clone lifecycle.
    // The main CharacterRouter remains responsible for catalog/UI/region routing.
    public sealed class NativeFutanariHairAdapter
    {
        private readonly Dictionary<string, Dictionary<int, int>> _cloneMapByPerson =
            new Dictionary<string, Dictionary<int, int>>(StringComparer.Ordinal);

        // v177: exact installed package UIDs learned from CharacterRouter's existing
        // AddonPackages catalog scan.  This resolves old .latest:/ references without
        // any recursive runtime package search (Lasseur.2BUN_Look is the confirmed case).
        private readonly Dictionary<string, string> _installedPackageUidByBase =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public void RegisterInstalledPackageUid(string packageUid)
        {
            if (string.IsNullOrEmpty(packageUid)) return;
            int dot = packageUid.LastIndexOf('.');
            if (dot <= 0 || dot + 1 >= packageUid.Length) return;
            int version;
            if (!int.TryParse(packageUid.Substring(dot + 1), out version)) return;
            string baseUid = packageUid.Substring(0, dot);

            string existing;
            if (_installedPackageUidByBase.TryGetValue(baseUid, out existing))
            {
                int edot = existing.LastIndexOf('.');
                int oldVersion = -1;
                if (edot > 0 && edot + 1 < existing.Length)
                    int.TryParse(existing.Substring(edot + 1), out oldVersion);
                if (oldVersion > version) return;
            }
            _installedPackageUidByBase[baseUid] = packageUid;
        }

        public string ResolveInstalledSourcePath(string path)
        {
            return ResolveInstalledPath(path);
        }

        public void ClearPerson(string uid)
        {
            if (!string.IsNullOrEmpty(uid))
                _cloneMapByPerson.Remove(uid);
        }

        private bool IsNodeEnabled(JSONNode item)
        {
            if (item == null) return false;
            string enabled = item["enabled"].Value;
            if (string.IsNullOrEmpty(enabled))
                return !string.IsNullOrEmpty(item["internalId"].Value) || !string.IsNullOrEmpty(item["id"].Value);
            if (string.Equals(enabled, "false", StringComparison.OrdinalIgnoreCase) || enabled == "0") return false;
            if (string.Equals(enabled, "true", StringComparison.OrdinalIgnoreCase) || enabled == "1") return true;
            return !string.IsNullOrEmpty(item["internalId"].Value) || !string.IsNullOrEmpty(item["id"].Value);
        }


        private bool IsFrameworkPlaceholderNode(JSONNode node)
        {
            if (node == null || !string.IsNullOrEmpty(node["internalId"].Value))
                return false;

            string id = node["id"].Value;
            if (string.IsNullOrEmpty(id))
                return false;

            string key = Key(id);
            return key == "haircreator" || key == "simv2hair";
        }

        private string Key(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            StringBuilder sb = new StringBuilder();
            string lower = value.ToLowerInvariant();
            for (int i=0;i<lower.Length;i++) if (char.IsLetterOrDigit(lower[i])) sb.Append(lower[i]);
            return sb.ToString();
        }

        private string TokenBag(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            List<string> tokens=new List<string>(); StringBuilder cur=new StringBuilder();
            string lower=value.ToLowerInvariant();
            for(int i=0;i<lower.Length;i++) { char c=lower[i]; if(char.IsLetterOrDigit(c)) cur.Append(c); else if(cur.Length>0){tokens.Add(cur.ToString());cur.Length=0;} }
            if(cur.Length>0) tokens.Add(cur.ToString()); if(tokens.Count<2) return ""; tokens.Sort(StringComparer.Ordinal); return string.Join("|",tokens.ToArray());
        }

        private string VariantAlias(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            List<string> tokens=new List<string>(); StringBuilder cur=new StringBuilder(); string lower=value.ToLowerInvariant();
            for(int i=0;i<lower.Length;i++){char c=lower[i];if(char.IsLetterOrDigit(c))cur.Append(c);else if(cur.Length>0){tokens.Add(cur.ToString());cur.Length=0;}}
            if(cur.Length>0)tokens.Add(cur.ToString());
            if(tokens.Count>=3){string last=tokens[tokens.Count-1];if(last.Length==1&&char.IsLetter(last[0]))tokens.RemoveAt(tokens.Count-1);}
            return string.Join("|",tokens.ToArray());
        }

        private string LooseAlias(string value)
        {
            if(string.IsNullOrEmpty(value))return "";
            List<string> tokens=new List<string>();StringBuilder cur=new StringBuilder();string lower=value.ToLowerInvariant();
            for(int i=0;i<lower.Length;i++){char c=lower[i];if(char.IsLetterOrDigit(c))cur.Append(c);else if(cur.Length>0){tokens.Add(cur.ToString());cur.Length=0;}}
            if(cur.Length>0)tokens.Add(cur.ToString());
            HashSet<string> generic=new HashSet<string>(StringComparer.OrdinalIgnoreCase){"look","hair","hairstyle","style","preset","custom","female","male"};
            List<string> kept=new List<string>();for(int i=0;i<tokens.Count;i++)if(!generic.Contains(tokens[i]))kept.Add(tokens[i]);
            if(kept.Count==0)return "";kept.Sort(StringComparer.Ordinal);return string.Join("|",kept.ToArray());
        }

        private List<string> Candidates(JSONNode node)
        {
            List<string> result=new List<string>(); if(node==null)return result;
            string internalId=node["internalId"].Value;
            if(!string.IsNullOrEmpty(internalId)){result.Add(internalId);int colon=internalId.IndexOf(':');if(colon>=0&&colon+1<internalId.Length)result.Add(internalId.Substring(colon+1));}
            string id=node["id"].Value;
            if(!string.IsNullOrEmpty(id)){
                result.Add(id);string p=id.Replace('\\','/');int slash=p.LastIndexOf('/');string file=slash>=0?p.Substring(slash+1):p;
                if(file.EndsWith(".vam",StringComparison.OrdinalIgnoreCase))file=file.Substring(0,file.Length-4);if(!string.IsNullOrEmpty(file))result.Add(file);
                string[] parts=p.Split('/');int parents=0;for(int i=parts.Length-2;i>=0&&parents<3;i--){string part=parts[i];if(string.IsNullOrEmpty(part)||string.Equals(part,"Custom",StringComparison.OrdinalIgnoreCase)||string.Equals(part,"Hair",StringComparison.OrdinalIgnoreCase)||string.Equals(part,"Female",StringComparison.OrdinalIgnoreCase)||string.Equals(part,"Male",StringComparison.OrdinalIgnoreCase))continue;if(!result.Contains(part))result.Add(part);parents++;}
            }
            return result;
        }

        private bool NameEquivalent(string a,string b)
        {
            if(string.IsNullOrEmpty(a)||string.IsNullOrEmpty(b))return false;
            if(string.Equals(a.Trim(),b.Trim(),StringComparison.OrdinalIgnoreCase))return true;
            string ak=Key(a),bk=Key(b);if(!string.IsNullOrEmpty(ak)&&ak==bk)return true;
            string at=TokenBag(a),bt=TokenBag(b);if(!string.IsNullOrEmpty(at)&&at==bt)return true;
            string av=VariantAlias(a),bv=VariantAlias(b);if(!string.IsNullOrEmpty(av)&&av==bv)return true;
            string al=LooseAlias(a),bl=LooseAlias(b);return !string.IsNullOrEmpty(al)&&al==bl;
        }

        private bool DisplayMatchesNode(string displayName,JSONNode node)
        {
            if(string.IsNullOrEmpty(displayName)||node==null)return false;
            List<string> candidates=Candidates(node);for(int i=0;i<candidates.Count;i++)if(NameEquivalent(displayName,candidates[i]))return true;return false;
        }

        private string ResolveInstalledPath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return path;

            try
            {
                string normalized = FileManagerSecure.NormalizeLoadPath(path);
                if (!string.IsNullOrEmpty(normalized))
                {
                    // Some VaM builds return the unresolved .latest:/ string unchanged.
                    // In that case continue into our catalog-backed exact-version map.
                    bool requestedLatest = path.IndexOf(".latest:/", StringComparison.OrdinalIgnoreCase) >= 0;
                    bool stillLatest = normalized.IndexOf(".latest:/", StringComparison.OrdinalIgnoreCase) >= 0;
                    if (!requestedLatest || !stillLatest)
                        return normalized;
                }
            }
            catch { }

            try
            {
                int sep = path.IndexOf(":/", StringComparison.Ordinal);
                if (sep <= 0)
                    return path;

                string uid = path.Substring(0, sep);
                if (!uid.EndsWith(".latest", StringComparison.OrdinalIgnoreCase))
                    return path;

                string baseUid = uid.Substring(0, uid.Length - 7);

                string installedUid;
                if (_installedPackageUidByBase.TryGetValue(baseUid, out installedUid) &&
                    !string.IsNullOrEmpty(installedUid))
                {
                    return installedUid + path.Substring(sep);
                }

                int v = FileManagerSecure.GetPackageVersion(baseUid);
                if (v > 0)
                    return baseUid + "." + v + path.Substring(sep);
            }
            catch { }

            return path;
        }

        private bool DisplayMatchesSourceVam(string displayName,JSONNode node)
        {
            if(string.IsNullOrEmpty(displayName)||node==null)return false;
            string path=ResolveInstalledPath(node["id"].Value);if(string.IsNullOrEmpty(path))return false;
            try{string content=FileManagerSecure.ReadAllText(path);if(string.IsNullOrEmpty(content))return false;JSONNode root=JSON.Parse(content);if(root==null)return false;string[] keys={"displayName","uid","id","name","storeName","itemName"};for(int i=0;i<keys.Length;i++){string c=root[keys[i]].Value;if(string.IsNullOrEmpty(c))continue;if(NameEquivalent(displayName,c))return true;int colon=c.IndexOf(':');if(colon>=0&&colon+1<c.Length&&NameEquivalent(displayName,c.Substring(colon+1)))return true;}}catch{}
            return false;
        }

        private bool SourceContains(DAZCharacterSelector selector,string desiredSex,JSONNode node)
        {
            if(selector==null||node==null||IsFrameworkPlaceholderNode(node))return false;

            // v178: if the source .vam is readable, its own displayName/uid is more
            // authoritative than directory-name aliases from the preset path.  This
            // matters for Lasseur 2BUN where SOFI Bun and Head both live below folders
            // containing "2BUN", but the actual items are "2BUN" and "2BUN Head".
            if(desiredSex=="F")
            {
                foreach(var hair in selector.femaleHairItems)
                {
                    if(hair==null||hair.name!="CustomHairItem"||string.IsNullOrEmpty(hair.displayName))continue;
                    if(DisplayMatchesSourceVam(hair.displayName,node))return true;
                }
                foreach(var hair in selector.femaleHairItems)
                {
                    if(hair==null||hair.name!="CustomHairItem"||string.IsNullOrEmpty(hair.displayName))continue;
                    if(DisplayMatchesNode(hair.displayName,node))return true;
                }
            }
            else if(desiredSex=="M")
            {
                foreach(var hair in selector.maleHairItems)
                {
                    if(hair==null||hair.name!="CustomHairItem"||string.IsNullOrEmpty(hair.displayName))continue;
                    if(DisplayMatchesSourceVam(hair.displayName,node))return true;
                }
                foreach(var hair in selector.maleHairItems)
                {
                    if(hair==null||hair.name!="CustomHairItem"||string.IsNullOrEmpty(hair.displayName))continue;
                    if(DisplayMatchesNode(hair.displayName,node))return true;
                }
            }
            return false;
        }

        public bool ResolveExactSourceClones(string personUid,DAZCharacterSelector selector,JSONArray desiredHair,string nativeSex,string desiredSex,out List<string> resolvedNames,out HashSet<int> preferredInstanceIds,out string error)
        {
            resolvedNames=new List<string>();preferredInstanceIds=new HashSet<int>();error="";
            if(selector==null||desiredHair==null){error="Character selector or Hair geometry unavailable.";return false;}
            Dictionary<int,int> cloneMap;if(!_cloneMapByPerson.TryGetValue(personUid??"",out cloneMap)){cloneMap=new Dictionary<int,int>();_cloneMapByPerson[personUid??""]=cloneMap;}
            List<string> missing=new List<string>();
            for(int pass=0;pass<2;pass++){
                missing.Clear();for(int i=0;i<desiredHair.Count;i++){JSONNode node=desiredHair[i];if(node==null||!IsNodeEnabled(node)||IsFrameworkPlaceholderNode(node))continue;if(!SourceContains(selector,desiredSex,node)){string internalId=node["internalId"].Value;string id=node["id"].Value;missing.Add(!string.IsNullOrEmpty(internalId)?internalId:id);}}
                if(missing.Count==0)break;
                if(pass==0){try{selector.InitHairItems();if(desiredSex=="F"){if(selector.hairSelectorFemaleUI!=null)selector.hairSelectorFemaleUI.Resync();}else{if(selector.hairSelectorMaleUI!=null)selector.hairSelectorMaleUI.Resync();}}catch{}}
            }
            if(missing.Count>0){int count=Math.Min(missing.Count,4);string[] shown=new string[count];for(int i=0;i<count;i++)shown[i]=missing[i];error="Unavailable Hair component"+(missing.Count==1?": ":"s: ")+string.Join(" | ",shown);return false;}
            bool addedAny=false;
            for(int i=0;i<desiredHair.Count;i++){
                JSONNode node=desiredHair[i];if(node==null||!IsNodeEnabled(node)||IsFrameworkPlaceholderNode(node))continue;bool found=false;
                if(nativeSex=="M"&&desiredSex=="F")
                {
                    // Pass 1: exact metadata from the referenced .vam.
                    foreach(var hair in selector.femaleHairItems)
                    {
                        if(hair==null||hair.name!="CustomHairItem"||!DisplayMatchesSourceVam(hair.displayName,node))continue;
                        found=true;
                        if(!resolvedNames.Contains(hair.displayName))resolvedNames.Add(hair.displayName);
                        int src=hair.GetInstanceID(),clone=0;bool alive=false;
                        if(cloneMap.TryGetValue(src,out clone)){foreach(var nh in selector.maleHairItems){if(nh!=null&&nh.GetInstanceID()==clone){alive=true;break;}}}
                        if(!alive){var newHair=UnityEngine.Object.Instantiate(hair,selector.maleHairContainer);try{newHair.active=false;}catch{}try{newHair.skin=selector.selectedCharacter.skin;}catch{}clone=newHair.GetInstanceID();cloneMap[src]=clone;addedAny=true;}
                        preferredInstanceIds.Add(clone);
                        break;
                    }
                    // Pass 2: legacy filename/folder aliases only when the .vam metadata
                    // did not identify a live source item.
                    if(!found)
                    {
                        foreach(var hair in selector.femaleHairItems)
                        {
                            if(hair==null||hair.name!="CustomHairItem"||!DisplayMatchesNode(hair.displayName,node))continue;
                            found=true;
                            if(!resolvedNames.Contains(hair.displayName))resolvedNames.Add(hair.displayName);
                            int src=hair.GetInstanceID(),clone=0;bool alive=false;
                            if(cloneMap.TryGetValue(src,out clone)){foreach(var nh in selector.maleHairItems){if(nh!=null&&nh.GetInstanceID()==clone){alive=true;break;}}}
                            if(!alive){var newHair=UnityEngine.Object.Instantiate(hair,selector.maleHairContainer);try{newHair.active=false;}catch{}try{newHair.skin=selector.selectedCharacter.skin;}catch{}clone=newHair.GetInstanceID();cloneMap[src]=clone;addedAny=true;}
                            preferredInstanceIds.Add(clone);
                            break;
                        }
                    }
                }
                else if(nativeSex=="F"&&desiredSex=="M")
                {
                    foreach(var hair in selector.maleHairItems)
                    {
                        if(hair==null||hair.name!="CustomHairItem"||!DisplayMatchesSourceVam(hair.displayName,node))continue;
                        found=true;
                        if(!resolvedNames.Contains(hair.displayName))resolvedNames.Add(hair.displayName);
                        int src=hair.GetInstanceID(),clone=0;bool alive=false;
                        if(cloneMap.TryGetValue(src,out clone)){foreach(var nh in selector.femaleHairItems){if(nh!=null&&nh.GetInstanceID()==clone){alive=true;break;}}}
                        if(!alive){var newHair=UnityEngine.Object.Instantiate(hair,selector.femaleHairContainer);try{newHair.active=false;}catch{}try{newHair.skin=selector.selectedCharacter.skin;}catch{}clone=newHair.GetInstanceID();cloneMap[src]=clone;addedAny=true;}
                        preferredInstanceIds.Add(clone);
                        break;
                    }
                    if(!found)
                    {
                        foreach(var hair in selector.maleHairItems)
                        {
                            if(hair==null||hair.name!="CustomHairItem"||!DisplayMatchesNode(hair.displayName,node))continue;
                            found=true;
                            if(!resolvedNames.Contains(hair.displayName))resolvedNames.Add(hair.displayName);
                            int src=hair.GetInstanceID(),clone=0;bool alive=false;
                            if(cloneMap.TryGetValue(src,out clone)){foreach(var nh in selector.femaleHairItems){if(nh!=null&&nh.GetInstanceID()==clone){alive=true;break;}}}
                            if(!alive){var newHair=UnityEngine.Object.Instantiate(hair,selector.femaleHairContainer);try{newHair.active=false;}catch{}try{newHair.skin=selector.selectedCharacter.skin;}catch{}clone=newHair.GetInstanceID();cloneMap[src]=clone;addedAny=true;}
                            preferredInstanceIds.Add(clone);
                            break;
                        }
                    }
                }
                if(!found){string internalId=node["internalId"].Value;string id=node["id"].Value;missing.Add(!string.IsNullOrEmpty(internalId)?internalId:id);}
            }
            if(missing.Count>0){int count=Math.Min(missing.Count,4);string[] shown=new string[count];for(int i=0;i<count;i++)shown[i]=missing[i];error="Unavailable Hair component"+(missing.Count==1?": ":"s: ")+string.Join(" | ",shown);return false;}
            if(addedAny){try{selector.InitHairItems();if(nativeSex=="M"){if(selector.hairSelectorMaleUI!=null)selector.hairSelectorMaleUI.Resync();}else{if(selector.hairSelectorFemaleUI!=null)selector.hairSelectorFemaleUI.Resync();}}catch(Exception e){error="Exact cross-gender Hair clone initialization failed: "+e.Message;return false;}}
            HashSet<int> live=new HashSet<int>();if(nativeSex=="M"){foreach(var hair in selector.maleHairItems)if(hair!=null&&preferredInstanceIds.Contains(hair.GetInstanceID()))live.Add(hair.GetInstanceID());}else{foreach(var hair in selector.femaleHairItems)if(hair!=null&&preferredInstanceIds.Contains(hair.GetInstanceID()))live.Add(hair.GetInstanceID());}preferredInstanceIds=live;
            if(resolvedNames.Count==0){error="Hair preset contains no enabled Hair components.";return false;}if(preferredInstanceIds.Count==0){error="Exact cross-gender Hair clones were not available after initialization.";return false;}return true;
        }

        // Experimental Stopper-style bank preparation retained but not used by v169 runtime path. Clone the complete opposite-gender
        // CustomHairItem bank into the native container once/on demand, then let VaM's
        // own HairPresets loader restore geometry, material and simulation state. This
        // is used as the preferred native-Futanari path; the exact-node resolver remains
        // available as a rollback/fallback for presets VaM still cannot load natively.
        public bool EnsureOppositeBankEnabled(
            string personUid,
            DAZCharacterSelector selector,
            string nativeSex,
            string desiredSex,
            out string error)
        {
            error = "";
            if (selector == null)
            {
                error = "Character selector unavailable.";
                return false;
            }
            if ((nativeSex != "M" && nativeSex != "F") ||
                (desiredSex != "M" && desiredSex != "F") ||
                nativeSex == desiredSex)
            {
                error = "Opposite-bank preparation requested for a non-cross-gender Hair load.";
                return false;
            }

            Dictionary<int, int> cloneMap;
            if (!_cloneMapByPerson.TryGetValue(personUid ?? "", out cloneMap))
            {
                cloneMap = new Dictionary<int, int>();
                _cloneMapByPerson[personUid ?? ""] = cloneMap;
            }

            bool addedAny = false;
            try
            {
                if (nativeSex == "M" && desiredSex == "F")
                {
                    foreach (var source in selector.femaleHairItems)
                    {
                        if (source == null || source.name != "CustomHairItem") continue;
                        int src = source.GetInstanceID();
                        int clone = 0;
                        bool alive = false;
                        if (cloneMap.TryGetValue(src, out clone))
                        {
                            foreach (var existing in selector.maleHairItems)
                            {
                                if (existing != null && existing.GetInstanceID() == clone)
                                {
                                    alive = true;
                                    break;
                                }
                            }
                        }
                        if (alive) continue;

                        var newHair = UnityEngine.Object.Instantiate(source, selector.maleHairContainer);
                        try { newHair.active = false; } catch { }
                        try { newHair.skin = selector.selectedCharacter.skin; } catch { }
                        cloneMap[src] = newHair.GetInstanceID();
                        addedAny = true;
                    }
                }
                else if (nativeSex == "F" && desiredSex == "M")
                {
                    foreach (var source in selector.maleHairItems)
                    {
                        if (source == null || source.name != "CustomHairItem") continue;
                        int src = source.GetInstanceID();
                        int clone = 0;
                        bool alive = false;
                        if (cloneMap.TryGetValue(src, out clone))
                        {
                            foreach (var existing in selector.femaleHairItems)
                            {
                                if (existing != null && existing.GetInstanceID() == clone)
                                {
                                    alive = true;
                                    break;
                                }
                            }
                        }
                        if (alive) continue;

                        var newHair = UnityEngine.Object.Instantiate(source, selector.femaleHairContainer);
                        try { newHair.active = false; } catch { }
                        try { newHair.skin = selector.selectedCharacter.skin; } catch { }
                        cloneMap[src] = newHair.GetInstanceID();
                        addedAny = true;
                    }
                }

                if (addedAny)
                {
                    selector.InitHairItems();
                    if (nativeSex == "M")
                    {
                        if (selector.hairSelectorMaleUI != null)
                            selector.hairSelectorMaleUI.Resync();
                    }
                    else
                    {
                        if (selector.hairSelectorFemaleUI != null)
                            selector.hairSelectorFemaleUI.Resync();
                    }
                }
            }
            catch (Exception e)
            {
                error = "Could not prepare opposite-gender Hair bank: " + e.Message;
                return false;
            }

            return true;
        }

        public void DeactivateOppositeBankRegion(DAZCharacterSelector selector,string nativeSex,string targetRegion,Func<string,string> resolveRegion)
        {
            if(selector==null||resolveRegion==null)return;try{
                if(nativeSex=="M"){foreach(var hair in selector.femaleHairItems){if(hair==null||!hair.active||string.IsNullOrEmpty(hair.displayName))continue;if(string.Equals(resolveRegion(hair.displayName),targetRegion,StringComparison.OrdinalIgnoreCase))selector.SetActiveHairItem(hair,false);}}
                else if(nativeSex=="F"){foreach(var hair in selector.maleHairItems){if(hair==null||!hair.active||string.IsNullOrEmpty(hair.displayName))continue;if(string.Equals(resolveRegion(hair.displayName),targetRegion,StringComparison.OrdinalIgnoreCase))selector.SetActiveHairItem(hair,false);}}
            }catch{}
        }
    }
}
