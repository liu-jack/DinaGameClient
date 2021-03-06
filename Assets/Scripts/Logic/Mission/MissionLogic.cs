using System;
using System.Collections.Generic;
using Assets.Scripts.Lib.Net;
using Assets.Scripts.Proto;
using Assets.Scripts.Utils;
using Assets.Scripts.Define;
using Assets.Scripts.Model.Player;
using Assets.Scripts.Lib.Loader;
using Assets.Scripts.View.Scene;
using Assets.Scripts.Manager;
using Assets.Scripts.Data;
using Assets.Scripts.Model.Scene;
using Assets.Scripts.Logic.Scene.SceneObject;
using UnityEngine;
using NetMessage;
using ProtoBuf;
using Assets.Scripts.View;
using Assets.Scripts.Logic.Scene;
using Assets.Scripts.Logic.RemoteCall;

namespace Assets.Scripts.Logic.Mission
{
    public class MissionLogic : BaseLogic
    {
        private List<int> hasCompleteIDs = new List<int>();
        private List<int> unCompleteIDs = new List<int>();
        private List<int> canAccpetIDs = new List<int>();

        private Dictionary<string, KMissionLoaclInfo> baseInfoList;
        private Dictionary<string, KMissionDialogue> dialogueList;
        private Dictionary<int, Dictionary<int, MissionInfo>> npcMissionList = new Dictionary<int, Dictionary<int, MissionInfo>>();
        private Dictionary<int, MissionInfo> currentMissionList = new Dictionary<int, MissionInfo>();
        private Dictionary<int, MissionInfo> canAcceptMissionList = new Dictionary<int, MissionInfo>();

        private static MissionLogic instance;
        public static MissionLogic GetInstance()
        {
            if (instance == null)
                instance = new MissionLogic();
            return instance;
        }

        protected override void Init()
        {

        }

        protected override void InitListeners()
        {
            RegistGameListener(ControllerCommand.PLAYER_LEVEL_UP, OnPlayerLevelUp);
        }

        public void SendAcceptQuestMsg(int missionID)
        {
			RemoteCallLogic.GetInstance().CallGS("OnAcceptQuestRequest", missionID);
        }

        public void SendCancelQuestMsg(int missionID)
        {
			RemoteCallLogic.GetInstance().CallGS("OnCancelQuestRequest", missionID);
        }

        public void SendFinishQuestMsg(int missionID)
        {	
			RemoteCallLogic.GetInstance().CallGS("OnFinishQuestRequest", missionID);
        }

        public void SendQuickFinishQuestMsg(int missionID)
        {
            RemoteCallLogic.GetInstance().CallGS("OnQuickFinishQuestRequest", missionID);
        }

		public void OnSyncQuestState(RemoteTable questState)
		{	
			int dwordBitCount = 32;			
			int[] missionIDs = GetAllMissionIDs();
			int indexPerDword = 0;
			int indexPerBit = 0;
			int dwordValue = 0;
			int nResult = 0;
			int leftShiftValue = 0;
			
			for (int i = 0; i < missionIDs.Length; ++i)
			{
				indexPerDword = missionIDs[i] / (dwordBitCount + 1);
				indexPerBit = missionIDs[i] % (dwordBitCount + 1);
				if (indexPerBit == 0)
					indexPerBit = 1;
				
				dwordValue = questState[indexPerDword + 1];
				leftShiftValue = 0x01 << (indexPerBit - 1);
				nResult = dwordValue & leftShiftValue;
				
				if (nResult == 0)
                {
                    if (missionIDs[i] != 0)
                        unCompleteIDs.Add(missionIDs[i]);
                }
                else
                {
                    hasCompleteIDs.Add(missionIDs[i]);
                }
			}
			
		}
		
		public void OnSyncQuestList(RemoteTable acceptQuests)
		{
			CheckPreMissionIsComplete();

            for (int i = 1; i <= acceptQuests.Count; i++)
            {
				RemoteTable acceptQuest = acceptQuests[i] as RemoteTable;
                WrapperMission(acceptQuest);
                //从可接列表中剔除已经接了任务.
                canAccpetIDs.Remove(acceptQuest["QuestID"]);
                RemoveFromCanAcceptList(acceptQuest["QuestID"]);
            }

            //生成当前可接任务.
            for (int i = 0; i < canAccpetIDs.Count; i++)
            {
                WrapperMission(canAccpetIDs[i]);
            }
			
		}
		
		public void OnSyncQuestValue(int questID, int valueIndex, int newValue)
		{
            KMissionLoaclInfo localInfo = baseInfoList[questID.ToString()];

            MissionInfo vo = GetMissionByID(questID);
            vo.condition = localInfo.Condition.Replace("V" + valueIndex, newValue.ToString());
            vo.curConditionNums[valueIndex] = newValue;
            if (CheckMissionFinish(vo))
            {
                vo.curStatus = MissionInfo.MisssionStatus.Finish;
                vo.tips = GetMissionLocalInfoByID(vo.id).QuestName + "<FFA200>(可提交)<->";
                UpdateCurrentMissionList(vo);
                SceneLogic.GetInstance().MainHero.property.CmdAutoAttack = false;

                if (vo.nPlotID == 0 && vo.type == (int)MissionInfo.MissionType.MainMission)
                {
                    EventDispatcher.GameWorld.Dispath(ControllerCommand.CONTINUE_MISSION);
                }

                if (vo.subType == (int)MissionInfo.MissionSubType.Monster || vo.subType == (int)MissionInfo.MissionSubType.Collect)
                {
                    PlayEffect("effect_ui_renwu_wanchengmubiao");
                }
                
            }
            else
            {
                vo.curStatus = MissionInfo.MisssionStatus.BeenAccepted;
                vo.tips = GetMissionLocalInfoByID(vo.id).QuestName + "<FF0000>(进行中)<->";
                UpdateCurrentMissionList(vo);
            }
		}
		
		public void OnSyncAcceptQuestRespond(int missionID, int resultCode)
		{
			if (resultCode == (int)KQuestResultCode.qrcSuccess)
            {
                PlayEffect("effect_ui_renwu_xinrenwu");
                MissionInfo vo = GetMissionByID(missionID);
                canAccpetIDs.Remove(missionID);
                RemoveFromCanAcceptList(missionID);
                if (vo == null)
                {
                    vo = WrapperMission(missionID);
                }
                vo.curStatus = MissionInfo.MisssionStatus.BeenAccepted;
                vo.tips = GetMissionLocalInfoByID(vo.id).QuestName + "(进行中)";
                UpdateCurrentMissionList(vo);

                if (vo.nPlotID != 0)
                {
                    EventDispatcher.GameWorld.Dispath(ControllerCommand.OPEN_PLOT_PANEL, vo.nPlotID);
                }
                else if (vo.type == (int)MissionInfo.MissionType.MainMission)
                {
                    EventDispatcher.GameWorld.Dispath(ControllerCommand.CONTINUE_MISSION);
                }
            }
            else
            {
                //错误提示.
            }
			
		}
		
		public void OnSyncFinishQuestRespond(int missionID, int resultCode)
		{
			if (resultCode == (int)KQuestResultCode.qrcSuccess)
            {
                bool bAutoContinue = false;
                MissionInfo vo = GetMissionByID(missionID);
                if (vo != null && vo.type == (int)MissionInfo.MissionType.MainMission)
                {
                    bAutoContinue = true;
                }

                hasCompleteIDs.Add(missionID);
                unCompleteIDs.Remove(missionID);
                RemoveFromCurrentList(missionID);

                CheckPreMissionIsComplete();
                PlayEffect("effect_ui_renwu_wanchengrenwu");

                if (bAutoContinue)
                {
                    EventDispatcher.GameWorld.Dispath(ControllerCommand.CONTINUE_MISSION);
                }
            }
            else
            {
                //错误提示.
            }
		}

        public void OnSyncQuickFinishQuestRespond(int missionID, int resultCode)
        {
            if (resultCode == (int)KQuestResultCode.qrcSuccess)
            {
                hasCompleteIDs.Add(missionID);
                unCompleteIDs.Remove(missionID);
                RemoveFromCurrentList(missionID);
                RemoveFromCanAcceptList(missionID);
                CheckPreMissionIsComplete();

                CollectObjLogic.GetInstance().m_bAutoCollect = false;
                PlayEffect("effect_ui_renwu_wanchengrenwu");
            }
        }

		public void OnSyncCancelQuestRespond(int missionID, int resultCode)
		{
			if (resultCode == (int)KQuestResultCode.qrcSuccess)
            {
                if (!canAccpetIDs.Contains(missionID))
                    canAccpetIDs.Add(missionID);

                //从当前任务列表删除.
                RemoveFromCurrentList(missionID);
                //添加到可接列表.
                WrapperMission(missionID);
            }
            else
            {
                //错误提示.
            }
			
		}

        private object OnPlayerLevelUp(System.Object obj)
        {
            CheckPreMissionIsComplete();
            return null;
        }

        private void CheckPreMissionIsComplete()
        {
            int roleLv = PlayerManager.GetInstance().MajorPlayer.level;
            int limtLv = 0;
            int preID;

            foreach (int missionID in unCompleteIDs)
            {
                preID = GetMissionLocalInfoByID(missionID).nPreID;
                limtLv = GetMissionLocalInfoByID(missionID).nLevelLimt;
                if (canAccpetIDs.Contains(missionID) && ((preID != 0 && !hasCompleteIDs.Contains(preID)) || roleLv < limtLv))
                {
                    canAccpetIDs.Remove(missionID);
                    RemoveFromCanAcceptList(missionID);
                }
                else if (!canAccpetIDs.Contains(missionID) && (preID == 0 || hasCompleteIDs.Contains(preID)))
                {
                    if (GetMissionByID(missionID) == null && !hasCompleteIDs.Contains(missionID))
                    {
                        canAccpetIDs.Add(missionID);
                        WrapperMission(missionID);
                    }
                }
            }
        }

        private void PlayEffect(string effectName)
        {
            AssetLoader.GetInstance().Load(URLUtil.GetEffectPath(effectName), EffectLoadComplete, AssetType.BUNDLER);
        }

        private void EffectLoadComplete(AssetInfo info)
        {
            Transform root = ViewManager.GetInstance().GetMissionViewEffectPos();
            //GameObject root = SceneLogic.GetInstance().MainHero.GetHeadPanel();
            if (root != null)
            {
                GameObject effect = GameObject.Instantiate(info.bundle.mainAsset) as GameObject;
                effect.transform.parent = root;
                effect.transform.localPosition = Vector3.zero;
                effect.transform.localScale = 700 * Vector3.one;
            }
        }

        private bool first = true;
        public void ShowWelcomeView()
        {
            int level = (int)PlayerManager.GetInstance().MajorPlayer.level;
            if (level == 1)
            {
                first = false;
                PathUtil.FindNpcAndOpen(2001);
            }
        }

        public void MissionLoadComplete()
        {
            baseInfoList = KConfigFileManager.GetInstance().missionLoaclInfoList.getAllData();
        }

        public void DialogueLoadComplete()
        {
            dialogueList = KConfigFileManager.GetInstance().missionDialogueList.getAllData();
        }

        public KMissionLoaclInfo GetMissionLocalInfoByID(int missionID)
        {
            return baseInfoList[missionID.ToString()];
		}
		
        public MissionInfo WrapperMission(RemoteTable pvo)
        {
			int index = pvo["QuestID"];
			string indexString = index.ToString();
			
			
            KMissionLoaclInfo localInfo = baseInfoList[indexString];
            if (localInfo == null)
                return null;

            MissionInfo vo = new MissionInfo();
            vo.curTimes = 1;
            vo.id = (int)pvo["QuestID"];
            vo.levelLimt = localInfo.nLevelLimt;
            vo.submitLv = localInfo.nSubmitLv;
            vo.preID = localInfo.nPreID;
            vo.type = localInfo.nType;
            vo.subType = localInfo.nSubType;
            vo.nPlotID = localInfo.nPlotID;
            vo.questName = localInfo.QuestName;
			vo.bScript = localInfo.bScript;
			
			RemoteTable questValueTable = (RemoteTable)pvo["QuestValue"];
			
            vo.curConditionNums = new int[questValueTable.Count];
            for (int i = 0; i < questValueTable.Count; i++)
            {
				int questValue = questValueTable[i + 1]; //lua那边table表默认是1开始的.
                vo.condition = localInfo.Condition.Replace("V" + i, questValue.ToString());
                vo.curConditionNums[i] = questValue;
            }

            bool hasCompeled = true;
            if (localInfo.ConditionNums != null && localInfo.ConditionNums != "")
            {
                string[] nums = localInfo.ConditionNums.Split(',');
                int len = nums.Length;
                if (len != questValueTable.Count)
                    Debug.Log("任务数据前端Condition和后端事件数不一致");

                int[] conditionNums = new int[len];
                for (int i = 0; i < len; ++i)
                {
                    conditionNums[i] = int.Parse(nums[i]);
                    if (conditionNums[i] != questValueTable[i+1])//lua那边table表默认是1开始的.
                        hasCompeled = false;
                }
                vo.conditionNums = conditionNums;
            }
            if (hasCompeled)
            {
                vo.curStatus = MissionInfo.MisssionStatus.Finish;
            }
            else
            {
                vo.curStatus = MissionInfo.MisssionStatus.BeenAccepted;
            }

            if (localInfo.NeedItemIDs != null && localInfo.NeedItemIDs != "")
            {
                string[] ids = localInfo.NeedItemIDs.Split(',');
                int len = ids.Length;
                int[] needItemIDs = new int[len];
                for (int i = 0; i < len; ++i)
                {
                    needItemIDs[i] = int.Parse(ids[i]);
                }
                vo.needItemIDs = needItemIDs;
            }
            if (localInfo.NeedItemNums != null && localInfo.NeedItemNums != "")
            {
                string[] nums = localInfo.NeedItemNums.Split(',');
                int len = nums.Length;
                int[] needItemNums = new int[len];
                for (int i = 0; i < len; ++i)
                {
                    needItemNums[i] = int.Parse(nums[i]);
                }
                vo.needItemNums = needItemNums;
            }
            vo.exp = localInfo.nRewardExp;
            vo.money = localInfo.nRewardMoney;
            vo.gold = localInfo.nRewardGold;
            if (localInfo.RewardItemIDs != null && localInfo.RewardItemIDs != "")
            {
                string[] ids = localInfo.RewardItemIDs.Split(',');
                int len = ids.Length;
                int[] rewardItemIDs = new int[len];
                for (int i = 0; i < len; ++i)
                {
                    rewardItemIDs[i] = int.Parse(ids[i]);
                }
                vo.rewardItemIDs = rewardItemIDs;
            }
            if (localInfo.Dialogue1 != null && localInfo.Dialogue1 != "")
            {
                string[] ids = localInfo.Dialogue1.Split(';');
                int len = ids.Length;
                int[] dialogueIDs = new int[len];
                for (int i = 0; i < len; ++i)
                {
                    dialogueIDs[i] = int.Parse(ids[i]);
                }
                vo.dialogue1 = dialogueIDs;
            }
            vo.dialogue2 = localInfo.nDialogue2;
            vo.dialogue3 = localInfo.nDialogue3;
            vo.npcID = localInfo.nNpcID;
            vo.submitNpcID = localInfo.nSubmitNpcID;
            vo.desc = localInfo.Describe;
            if (vo.curStatus == MissionInfo.MisssionStatus.Accept)
                vo.tips = localInfo.QuestName + "<78FF00>(可接)<->";
            else if (vo.curStatus == MissionInfo.MisssionStatus.BeenAccepted)
                vo.tips = localInfo.QuestName + "<FF0000>(未完成)<->";
            else if (vo.curStatus == MissionInfo.MisssionStatus.Finish)
                vo.tips = localInfo.QuestName + "<FFA2000>(可提交)<->";
            if (localInfo.Position != null && localInfo.Position != "0")
            {
                string[] pos = localInfo.Position.Split(';');
                int len = pos.Length;
                int[] position = new int[len];
                for (int i = 0; i < len; ++i)
                {
                    position[i] = int.Parse(pos[i]);
                }
                vo.position = position;
            }
            vo.pathAiType = localInfo.PathAIType;
            vo.pathType = localInfo.PathType;
            vo.isAutoComplete = localInfo.bAutoComplete;// == 0 ? false : true;
            vo.totalTimes = localInfo.nTimes;

            UpdateCurrentMissionList(vo);

            return vo;
        }

        public MissionInfo WrapperMission(int missionID)
        {
            KMissionLoaclInfo localInfo = baseInfoList[missionID.ToString()];
            if (localInfo == null)
                return null;

            MissionInfo vo = new MissionInfo();
            vo.curTimes = 1;
            vo.id = missionID;
            vo.levelLimt = localInfo.nLevelLimt;
            vo.submitLv = localInfo.nSubmitLv;
            vo.preID = localInfo.nPreID;
            vo.type = localInfo.nType;
            vo.subType = localInfo.nSubType;
            vo.nPlotID = localInfo.nPlotID;
            vo.questName = localInfo.QuestName;
			vo.bScript = localInfo.bScript;
			
            if (localInfo.ConditionNums != null && localInfo.ConditionNums != "")
            {
                string[] nums = localInfo.ConditionNums.Split(',');
                int len = nums.Length;
                int[] conditionNums = new int[len];
                for (int i = 0; i < len; ++i)
                {
                    conditionNums[i] = int.Parse(nums[i]);
                    vo.condition = localInfo.Condition.Replace("V" + i, "0");
                }
                vo.conditionNums = conditionNums;
                vo.curConditionNums = new int[conditionNums.Length];
            }
            vo.curStatus = MissionInfo.MisssionStatus.Accept;

            if (localInfo.NeedItemIDs != null && localInfo.NeedItemIDs != "")
            {
                string[] ids = localInfo.NeedItemIDs.Split(',');
                int len = ids.Length;
                int[] needItemIDs = new int[len];
                for (int i = 0; i < len; ++i)
                {
                    needItemIDs[i] = int.Parse(ids[i]);
                }
                vo.needItemIDs = needItemIDs;
            }
            if (localInfo.NeedItemNums != null && localInfo.NeedItemNums != "")
            {
                string[] nums = localInfo.NeedItemNums.Split(',');
                int len = nums.Length;
                int[] needItemNums = new int[len];
                for (int i = 0; i < len; ++i)
                {
                    needItemNums[i] = int.Parse(nums[i]);
                }
                vo.needItemNums = needItemNums;
            }
            vo.exp = localInfo.nRewardExp;
            vo.money = localInfo.nRewardMoney;
            vo.gold = localInfo.nRewardGold;
            if (localInfo.RewardItemTypes != null && localInfo.RewardItemTypes != "" && localInfo.RewardItemTypes != "0")
            {
                string[] ids = localInfo.RewardItemTypes.Split(',');
                int len = ids.Length;
                int[] types = new int[len];
                for (int i = 0; i < len; ++i)
                {
                    types[i] = int.Parse(ids[i]);
                }
                vo.rewardTypes = types;
            }
            if (localInfo.RewardItemIDs != null && localInfo.RewardItemIDs != "" && localInfo.RewardItemIDs != "0")
            {
                string[] ids = localInfo.RewardItemIDs.Split(',');
                int len = ids.Length;
                int[] rewardItemIDs = new int[len];
                for (int i = 0; i < len; ++i)
                {
                    rewardItemIDs[i] = int.Parse(ids[i]);
                }
                vo.rewardItemIDs = rewardItemIDs;
            }
			
			if (localInfo.RewardItemNums != null && localInfo.RewardItemNums != "" && localInfo.RewardItemNums != "0")
            {
                string[] nums = localInfo.RewardItemNums.Split(',');
                int len = nums.Length;
                int[] rewardItemNums = new int[len];
                for (int i = 0; i < len; ++i)
                {
                    rewardItemNums[i] = int.Parse(nums[i]);
                }
                vo.rewardItemNums = rewardItemNums;
            }
			
            if (localInfo.Dialogue1 != null && localInfo.Dialogue1 != "")
            {
                string[] ids = localInfo.Dialogue1.Split(',');
                int len = ids.Length;
                int[] dialogueIDs = new int[len];
                for (int i = 0; i < len; ++i)
                {
                    dialogueIDs[i] = int.Parse(ids[i]);
                }
                vo.dialogue1 = dialogueIDs;
            }
            vo.dialogue2 = localInfo.nDialogue2;
            vo.dialogue3 = localInfo.nDialogue3;
            vo.npcID = localInfo.nNpcID;
            vo.submitNpcID = localInfo.nSubmitNpcID;
            vo.desc = localInfo.Describe;
            vo.tips = localInfo.QuestName + "<78FF00>(可接)<->";
            if (localInfo.Position != null && localInfo.Position != "0")
            {
                string[] pos = localInfo.Position.Split(';');
                int len = pos.Length;
                int[] position = new int[len];
                for (int i = 0; i < len; ++i)
                {
                    position[i] = int.Parse(pos[i]);
                }
                vo.position = position;
            }
            vo.pathAiType = localInfo.PathAIType;
            vo.pathType = localInfo.PathType;
            vo.isAutoComplete = localInfo.bAutoComplete;
            vo.totalTimes = localInfo.nTimes;

            UpdateCanAcceptList(vo);

            return vo;
        }

        private void UpdateNpcSign(int npcID)
        {
            //取NPC太蛋疼。。。.
            uint npcSceneID = SceneLogic.GetInstance().GetNpcSceneID(npcID);
            if (npcSceneID != 0)
            {
                SceneEntity npc = SceneLogic.GetInstance().GetSceneObject(npcSceneID) as SceneEntity;
                if (npc != null)
                {
                    npc.DispatchEvent(ControllerCommand.UPDATE_MISSION_SIGN);
                }
            }
        }

        public void UpdateNpcMission(MissionInfo vo)
        {
            if (vo.curStatus == MissionInfo.MisssionStatus.Accept)
            {
                if (!npcMissionList.ContainsKey(vo.npcID))
                    npcMissionList[vo.npcID] = new Dictionary<int, MissionInfo>();
                npcMissionList[vo.npcID][vo.id] = vo;
                UpdateNpcSign(vo.npcID);
                EventDispatcher.GameWorld.Dispath(ControllerCommand.UPDATE_MISSION, new object());
            }
            else if (vo.curStatus == MissionInfo.MisssionStatus.Finish || vo.curStatus == MissionInfo.MisssionStatus.BeenAccepted)
            {
                if (!npcMissionList.ContainsKey(vo.submitNpcID))
                    npcMissionList[vo.submitNpcID] = new Dictionary<int, MissionInfo>();
                npcMissionList[vo.submitNpcID][vo.id] = vo;
                UpdateNpcSign(vo.submitNpcID);
                EventDispatcher.GameWorld.Dispath(ControllerCommand.UPDATE_MISSION, new object());
            }
        }

        public void UpdateCurrentMissionList(MissionInfo vo)
        {
            if (!currentMissionList.ContainsKey(vo.id))
            {
                currentMissionList.Add(vo.id, vo);
            }
            else
            {
                currentMissionList[vo.id] = vo;
            }
            UpdateNpcMission(vo);
        }

        public void RemoveNPCMission(int delMissionID)
        {
            KMissionLoaclInfo localInfo = baseInfoList[delMissionID.ToString()];

            if (localInfo != null)
            {
                RemoveNpcMission(localInfo.nNpcID, delMissionID);
                RemoveNpcMission(localInfo.nSubmitNpcID, delMissionID);
            }
        }

        public void RemoveFromCurrentList(int delMissionID)
        {
            if (currentMissionList.ContainsKey(delMissionID))
            {
                MissionInfo vo = GetMissionByID(delMissionID);
                if (vo != null)
                {
                    if (vo.curStatus == MissionInfo.MisssionStatus.Finish)
                        RemoveNpcMission(vo.submitNpcID, delMissionID);
                }

                currentMissionList.Remove(delMissionID);
            }
        }

        public Dictionary<int, MissionInfo> GetCurrentMissionList()
        {
            return currentMissionList;
        }

        public void UpdateCanAcceptList(MissionInfo vo)
        {
            if (!canAcceptMissionList.ContainsKey(vo.id))
            {
                canAcceptMissionList.Add(vo.id, vo);
            }
            else
            {
                canAcceptMissionList[vo.id] = vo;
            }
            UpdateNpcMission(vo);
        }

        public void RemoveFromCanAcceptList(int delMissionID)
        {
            if (canAcceptMissionList.ContainsKey(delMissionID))
            {
                MissionInfo vo = GetMissionByID(delMissionID);
                if (vo != null)
                {
                    canAcceptMissionList.Remove(delMissionID);
                    RemoveNpcMission(vo.npcID, delMissionID);
                }
            }
        }

        public Dictionary<int, MissionInfo> GetCanAcceptList()
        {
            return canAcceptMissionList;
        }

        public MissionInfo GetMissionByID(int missionID)
        {
            if (canAcceptMissionList.ContainsKey(missionID))
            {
                return canAcceptMissionList[missionID];
            }
            else if (currentMissionList.ContainsKey(missionID))
            {
                return currentMissionList[missionID];
            }

            return null;
        }

        private void RemoveNpcMission(int npcID, int missionID)
        {
            if (npcMissionList.ContainsKey(npcID))
            {
                if (npcMissionList[npcID].ContainsKey(missionID))
                {
                    npcMissionList[npcID].Remove(missionID);
                    UpdateNpcSign(npcID);
                    EventDispatcher.GameWorld.Dispath(ControllerCommand.UPDATE_MISSION, new object());
                }
            }
        }

        public Dictionary<int, MissionInfo> GetNpcMissionList(int npcID)
        {
            if (npcMissionList.ContainsKey(npcID))
                return npcMissionList[npcID];

            return null;
        }

        public int[] GetAllMissionIDs()
        {
            List<int> ids = new List<int>();
            foreach (string id in baseInfoList.Keys)
            {
                ids.Add(int.Parse(id));
            }
            return ids.ToArray();
        }

        public bool CheckMissionFinish(MissionInfo vo)
        {
            if (vo.curConditionNums.Length != vo.conditionNums.Length)
                return false;
            for (int i = 0; i < vo.curConditionNums.Length; i++)
            {
                if (vo.curConditionNums[i] < vo.conditionNums[i])
                    return false;
            }

            return true;
		}
    }
}
