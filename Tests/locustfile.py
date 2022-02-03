from locust import HttpUser, task, events, between, tag
from locust.runners import MasterRunner
import uuid

@events.test_start.add_listener
def on_test_start(environment, **kwargs):
	print("Test starting...")

@events.test_stop.add_listener
def on_test_stop(environment, **kwargs):
	print("Test completed.")

@events.init.add_listener
def on_locust_init(environment, **kwargs):
	if isinstance(environment.runner, MasterRunner):
		print("Master node init")
	else:
		print("Worker or standalone init")

class PlayerServiceUser(HttpUser):
	token = ""
	installId = ""
	accountId = ""
# 	wait_time = between(1, 5) # Random time, in seconds, between each action.  Re-evaluated after every task.

# 	def on_start(self):
# 		self.installId = "locust-" + uuid.uuid4().hex
# 		print("Initializing user with UUID installId: " + self.installId)
# 		self.launch()

	@tag("nuke")
	@task(1)
	def spam(self):
		self.installId = "locust-" + uuid.uuid4().hex
		self.launch()
		print("InstallId: " + self.installId + " | " + "AccountId: " + self.accountId)
		self.nuke_items()
# 		self.environment.runner.quit()

	def launch(self):
		response = self.client.post("/player/v2/launch", name = "/launch", json = 
		{
			"installId": self.installId
		}).json()
		self.token = response["accessToken"]
# 		print(self.installId)
		self.accountId = response["player"]["id"]

	@tag("standard")
	@task(1)
	def update(self):
		self.client.headers["Authorization"] = "Bearer " + self.token
		response = self.client.patch("/player/v2/update", json =
		{
			"components": [
				{
					"name": "abTest",
					"data": "{\"testGroups\":[],\"component\":{\"isDirty\":true,\"version\":1}}"
				},
				{
					"name": "hero",
					"data": "{\"heroes\":[],\"heroIds\":[\"human_infantryman\",\"elven_crossbow_recruit\"],\"teams\":[{\"id\":\"f46bfd851caa7267810463b87ac81f77\",\"name\":\"Team 1\",\"teamSlot\":0,\"heroIds\":[\"human_infantryman\",\"elven_crossbow_recruit\"],\"isAutoPlayTeam\":false},{\"id\":\"62893ba2977cf78482612b88ab61bd1e\",\"name\":\" Team 2\",\"teamSlot\":1,\"heroIds\":[],\"isAutoPlayTeam\":false},{\"id\":\"0a780bc64ce11f58a3f8fa89771b021d\",\"name\":\" Team 3\",\"teamSlot\":2,\"heroIds\":[],\"isAutoPlayTeam\":false},{\"id\":\"7d5de5b56c93a2bbd266023f8b4efc98\",\"name\":\" Team 4\",\"teamSlot\":3,\"heroIds\":[],\"isAutoPlayTeam\":false},{\"id\":\"d23065a6b6dc55beb2b0e13de816a259\",\"name\":\" Team 5\",\"teamSlot\":4,\"heroIds\":[],\"isAutoPlayTeam\":false}],\"component\":{\"isDirty\":true,\"version\":3}}"
				},
				{
					"name": "wallet",
					"data": "{\"currencies\":[{\"currencyId\":\"energy\",\"amount\":72},{\"currencyId\":\"hard_currency\",\"amount\":100},{\"currencyId\":\"soft_currency\",\"amount\":25},{\"currencyId\":\"xp_currency\",\"amount\":125},{\"currencyId\":\"username_change\",\"amount\":1}],\"component\":{\"isDirty\":true,\"version\":1}}"
				},
				{
					"name": "account",
					"data": "{\"accountLevel\":1,\"lastEnergyRegenTime\":\"132866786242999490\",\"lastDungeonKeyRegenTime\":\"132866786242999490\",\"lastOfflineTime\":\"132866786242999490\",\"lastDailyResetTime\":\"132866786242999490\",\"lastCalendarLoginTime\":\"132866786242999490\",\"accountCreationDate\":\"132866786242999490\",\"accountName\":\"Player19944066\",\"accountAvatar\":\"human_infantryman\",\"lifetimeSessionCount\":0,\"sentInstallEvent\":false,\"migrated3DayCalendarData\":false,\"useActionCams\":true,\"component\":{\"isDirty\":true,\"version\":9},\"timeOffset\":{\"days\":0,\"hours\":0,\"minutes\":0},\"seenEntities\":[],\"calendarRewards\":[],\"bannerPulls\":[],\"dynamicTimespans\":[],\"hasDebugPermissions\":false,\"hasLocalNotificationsAuth\":false,\"patrolMinutesChecked\":0,\"patrolAccumulatedRewards\":[],\"patrolFlatRewardLevelsClaimed\":[],\"tutorialRecords\":[]}"
				},
				{
					"name": "equipment",
					"data": "{\"equipment\":[],\"equipmentIds\":[],\"inventoryLevel\":0,\"component\":{\"isDirty\":true,\"version\":2}}"
				},
				{
					"name": "world",
					"data": "{\"campaigns\":[],\"lockedLevels\":[],\"component\":{\"isDirty\":true,\"version\":2},\"activeBattles\":[],\"teamsUsed\":[],\"starRewards\":[],\"autoPlayLogLevelIds\":[],\"lastLevelCurrencyUsed\":[],\"dungeonPasses\":[],\"lastTeamUsedId\":\"\",\"levelRuns\":[]}"
				},
				{
					"name": "tutorial",
					"data": "{\"component\":{\"isDirty\":true,\"version\":3},\"tutorialRecords\":[],\"tutorialProgressionTracker\":{\"LevelupsSinceMetagame1\":0},\"tutorialFlags\":[]}"
				},
				{
					"name": "quest",
					"data": "{\"collectedQuests\":[],\"startedQuests\":[],\"progressedQuests\":[],\"questProgress\":[],\"dailyResets\":[],\"questTimespans\":[],\"questTimespansV2\":[],\"component\":{\"isDirty\":true,\"version\":3}}"
				},
				{
					"name": "store",
					"data": "{\"purchases\":[],\"pendingTransactions\":[],\"lifetimePurchasedStoreOfferIds\":[],\"popUpInfos\":[],\"component\":{\"isDirty\":true,\"version\":1},\"hasMadeRealMoneyTransaction\":false}"
				},
				{
					"name": "multiplayer",
					"data": "{\"component\":{\"isDirty\":true,\"version\":1},\"currentMatch\":null}"
				}
			],
			"items": [
				{
					"iid": "hero_human_infantryman",
					"type": "hero",
					"data": "{\"id\":\"human_infantryman\",\"level\":1,\"ascension\":0,\"evolution\":1,\"skill\":1,\"equipment\":[]}",
					"delete": False
				},
				{
					"iid": "hero_elven_crossbow_recruit",
					"type": "hero",
					"data": "{\"id\":\"elven_crossbow_recruit\",\"level\":1,\"ascension\":0,\"evolution\":1,\"skill\":1,\"equipment\":[]}",
					"delete": False
				}
			],
			"screenName": "Player19944066",
			"game": "57901c6df82a45708018ba73b8d16004",
			"secret": "72d0676767714480b1e4cec845105332"
		}, name = "/update")
		
	@tag("nuke")
	@task(0)
	def nuke_items(self):
		self.client.headers["Authorization"] = "Bearer " + self.token
		response = self.client.patch("/player/v2/update", json =
		{
			"components": [
				{
					"name": "abTest",
					"data": "{\"testGroups\":[],\"component\":{\"isDirty\":true,\"version\":1}}"
				},
				{
					"name": "hero",
					"data": "{\"heroes\":[],\"heroIds\":[\"human_infantryman\",\"elven_crossbow_recruit\"],\"teams\":[{\"id\":\"f46bfd851caa7267810463b87ac81f77\",\"name\":\"Team 1\",\"teamSlot\":0,\"heroIds\":[\"human_infantryman\",\"elven_crossbow_recruit\"],\"isAutoPlayTeam\":false},{\"id\":\"62893ba2977cf78482612b88ab61bd1e\",\"name\":\" Team 2\",\"teamSlot\":1,\"heroIds\":[],\"isAutoPlayTeam\":false},{\"id\":\"0a780bc64ce11f58a3f8fa89771b021d\",\"name\":\" Team 3\",\"teamSlot\":2,\"heroIds\":[],\"isAutoPlayTeam\":false},{\"id\":\"7d5de5b56c93a2bbd266023f8b4efc98\",\"name\":\" Team 4\",\"teamSlot\":3,\"heroIds\":[],\"isAutoPlayTeam\":false},{\"id\":\"d23065a6b6dc55beb2b0e13de816a259\",\"name\":\" Team 5\",\"teamSlot\":4,\"heroIds\":[],\"isAutoPlayTeam\":false}],\"component\":{\"isDirty\":true,\"version\":3}}"
				},
				{
					"name": "wallet",
					"data": "{\"currencies\":[{\"currencyId\":\"energy\",\"amount\":72},{\"currencyId\":\"hard_currency\",\"amount\":100},{\"currencyId\":\"soft_currency\",\"amount\":25},{\"currencyId\":\"xp_currency\",\"amount\":125},{\"currencyId\":\"username_change\",\"amount\":1}],\"component\":{\"isDirty\":true,\"version\":1}}"
				},
				{
					"name": "account",
					"data": "{\"accountLevel\":1,\"lastEnergyRegenTime\":\"132866786242999490\",\"lastDungeonKeyRegenTime\":\"132866786242999490\",\"lastOfflineTime\":\"132866786242999490\",\"lastDailyResetTime\":\"132866786242999490\",\"lastCalendarLoginTime\":\"132866786242999490\",\"accountCreationDate\":\"132866786242999490\",\"accountName\":\"Player19944066\",\"accountAvatar\":\"human_infantryman\",\"lifetimeSessionCount\":0,\"sentInstallEvent\":false,\"migrated3DayCalendarData\":false,\"useActionCams\":true,\"component\":{\"isDirty\":true,\"version\":9},\"timeOffset\":{\"days\":0,\"hours\":0,\"minutes\":0},\"seenEntities\":[],\"calendarRewards\":[],\"bannerPulls\":[],\"dynamicTimespans\":[],\"hasDebugPermissions\":false,\"hasLocalNotificationsAuth\":false,\"patrolMinutesChecked\":0,\"patrolAccumulatedRewards\":[],\"patrolFlatRewardLevelsClaimed\":[],\"tutorialRecords\":[]}"
				},
				{
					"name": "equipment",
					"data": "{\"equipment\":[],\"equipmentIds\":[],\"inventoryLevel\":0,\"component\":{\"isDirty\":true,\"version\":2}}"
				},
				{
					"name": "world",
					"data": "{\"campaigns\":[],\"lockedLevels\":[],\"component\":{\"isDirty\":true,\"version\":2},\"activeBattles\":[],\"teamsUsed\":[],\"starRewards\":[],\"autoPlayLogLevelIds\":[],\"lastLevelCurrencyUsed\":[],\"dungeonPasses\":[],\"lastTeamUsedId\":\"\",\"levelRuns\":[]}"
				},
				{
					"name": "tutorial",
					"data": "{\"component\":{\"isDirty\":true,\"version\":3},\"tutorialRecords\":[],\"tutorialProgressionTracker\":{\"LevelupsSinceMetagame1\":0},\"tutorialFlags\":[]}"
				},
				{
					"name": "quest",
					"data": "{\"collectedQuests\":[],\"startedQuests\":[],\"progressedQuests\":[],\"questProgress\":[],\"dailyResets\":[],\"questTimespans\":[],\"questTimespansV2\":[],\"component\":{\"isDirty\":true,\"version\":3}}"
				},
				{
					"name": "store",
					"data": "{\"purchases\":[],\"pendingTransactions\":[],\"lifetimePurchasedStoreOfferIds\":[],\"popUpInfos\":[],\"component\":{\"isDirty\":true,\"version\":1},\"hasMadeRealMoneyTransaction\":false}"
				},
				{
					"name": "multiplayer",
					"data": "{\"component\":{\"isDirty\":true,\"version\":1},\"currentMatch\":null}"
				}
			],
			"items": [
				{
					"aid": self.accountId,
					"iid": "hero_human_infantryman",
					"data": {
						"id": "human_infantryman",
						"level": 1,
						"ascension": 0,
						"evolution": 1,
						"skill": 1,
						"equipment": []
					},
					"type": "hero"
				},
				{
					"aid": self.accountId,
					"iid": "levelRunInfo_campaign_01_01",
					"data": {
						"levelId": "campaign_01_01",
						"completionTime": 15,
						"stars": 3,
						"failedAttempts": 0,
						"attempts": 1
					},
					"type": "levelRunInfo"
				},
				{
					"aid": self.accountId,
					"iid": "hero_elven_crossbow_recruit",
					"data": {
						"id": "elven_crossbow_recruit",
						"level": 1,
						"ascension": 2,
						"evolution": 2,
						"skill": 1,
						"equipment": []
					},
					"type": "hero"
				},
				{
					"aid": self.accountId,
					"iid": "levelRunInfo_campaign_01_02",
					"data": {
						"levelId": "campaign_01_02",
						"completionTime": 30,
						"stars": 3,
						"failedAttempts": 0,
						"attempts": 1
					},
					"type": "levelRunInfo"
				},
				{
					"aid": self.accountId,
					"iid": "hero_orc_fam1_bow",
					"data": {
						"id": "orc_fam1_bow",
						"level": 1,
						"ascension": 0,
						"evolution": 1,
						"skill": 1,
						"equipment": []
					},
					"type": "hero"
				},
				{
					"aid": self.accountId,
					"iid": "hero_demon_healer",
					"data": {
						"id": "demon_healer",
						"level": 1,
						"ascension": 0,
						"evolution": 1,
						"skill": 1,
						"equipment": []
					},
					"type": "hero"
				},
				{
					"aid": self.accountId,
					"iid": "levelRunInfo_campaign_01_03",
					"data": {
						"levelId": "campaign_01_03",
						"completionTime": 35,
						"stars": 3,
						"failedAttempts": 0,
						"attempts": 1
					},
					"type": "levelRunInfo"
				},
				{
					"aid": self.accountId,
					"iid": "hero_human_noblewoman",
					"data": {
						"id": "human_noblewoman",
						"level": 2,
						"ascension": 0,
						"evolution": 1,
						"skill": 1,
						"equipment": []
					},
					"type": "hero"
				},
				{
					"aid": self.accountId,
					"iid": "levelRunInfo_campaign_01_04",
					"data": {
						"levelId": "campaign_01_04",
						"completionTime": 67,
						"stars": 3,
						"failedAttempts": 0,
						"attempts": 1
					},
					"type": "levelRunInfo"
				},
				{
					"aid": self.accountId,
					"iid": "hero_elven_noble_protector",
					"data": {
						"id": "elven_noble_protector",
						"level": 1,
						"ascension": 0,
						"evolution": 1,
						"skill": 1,
						"equipment": []
					},
					"type": "hero"
				},
				{
					"aid": self.accountId,
					"iid": "levelRunInfo_campaign_01_05",
					"data": {
						"levelId": "campaign_01_05",
						"completionTime": 68,
						"stars": 3,
						"failedAttempts": 1,
						"attempts": 2
					},
					"type": "levelRunInfo"
				},
				{
					"aid": self.accountId,
					"iid": "levelRunInfo_campaign_01_06",
					"data": {
						"levelId": "campaign_01_06",
						"completionTime": 59,
						"stars": 3,
						"failedAttempts": 3,
						"attempts": 7
					},
					"type": "levelRunInfo"
				},
				{
					"aid": self.accountId,
					"iid": "hero_demon_axe_thrower",
					"data": {
						"id": "demon_axe_thrower",
						"level": 1,
						"ascension": 0,
						"evolution": 4,
						"skill": 1,
						"equipment": []
					},
					"type": "hero"
				},
				{
					"aid": self.accountId,
					"iid": "hero_werewolf_berserker",
					"data": {
						"id": "werewolf_berserker",
						"level": 1,
						"ascension": 0,
						"evolution": 1,
						"skill": 1,
						"equipment": []
					},
					"type": "hero"
				},
				{
					"aid": self.accountId,
					"iid": "levelRunInfo_campaign_01_07",
					"data": {
						"levelId": "campaign_01_07",
						"completionTime": 102,
						"stars": 3,
						"failedAttempts": 3,
						"attempts": 4
					},
					"type": "levelRunInfo"
				},
				{
					"aid": self.accountId,
					"iid": "levelRunInfo_campaign_01_08",
					"data": {
						"levelId": "campaign_01_08",
						"completionTime": 113,
						"stars": 3,
						"failedAttempts": 0,
						"attempts": 1
					},
					"type": "levelRunInfo"
				},
				{
					"aid": self.accountId,
					"iid": "levelRunInfo_campaign_01_09",
					"data": {
						"levelId": "campaign_01_09",
						"completionTime": 118,
						"stars": 3,
						"failedAttempts": 2,
						"attempts": 3
					},
					"type": "levelRunInfo"
				},
				{
					"aid": self.accountId,
					"iid": "levelRunInfo_campaign_01_10",
					"data": {
						"levelId": "campaign_01_10",
						"completionTime": 140,
						"stars": 3,
						"failedAttempts": 3,
						"attempts": 7
					},
					"type": "levelRunInfo"
				},
				{
					"aid": self.accountId,
					"iid": "hero_orc_fam1_warrior",
					"data": {
						"id": "orc_fam1_warrior",
						"level": 1,
						"ascension": 0,
						"evolution": 1,
						"skill": 1,
						"equipment": []
					},
					"type": "hero"
				},
				{
					"aid": self.accountId,
					"iid": "levelRunInfo_campaign_02_01",
					"data": {
						"levelId": "campaign_02_01",
						"completionTime": 121,
						"stars": 3,
						"failedAttempts": 0,
						"attempts": 1
					},
					"type": "levelRunInfo"
				},
				{
					"aid": self.accountId,
					"iid": "levelRunInfo_campaign_02_02",
					"data": {
						"levelId": "campaign_02_02",
						"completionTime": -1,
						"stars": 3,
						"failedAttempts": 2,
						"attempts": 3
					},
					"type": "levelRunInfo"
				},
				{
					"aid": self.accountId,
					"iid": "hero_orc_fam1_crossbow",
					"data": {
						"id": "orc_fam1_crossbow",
						"level": 1,
						"ascension": 0,
						"evolution": 1,
						"skill": 1,
						"equipment": []
					},
					"type": "hero"
				},
				{
					"aid": self.accountId,
					"iid": "hero_demon_cleaver",
					"data": {
						"id": "demon_cleaver",
						"level": 1,
						"ascension": 0,
						"evolution": 1,
						"skill": 1,
						"equipment": []
					},
					"type": "hero"
				},
				{
					"aid": self.accountId,
					"iid": "hero_human_crystal_tank",
					"data": {
						"id": "human_crystal_tank",
						"level": 1,
						"ascension": 0,
						"evolution": 1,
						"skill": 1,
						"equipment": []
					},
					"type": "hero"
				},
				{
					"aid": self.accountId,
					"iid": "hero_orc_reaver",
					"data": {
						"id": "orc_reaver",
						"level": 1,
						"ascension": 0,
						"evolution": 1,
						"skill": 1,
						"equipment": []
					},
					"type": "hero"
				},
				{
					"aid": self.accountId,
					"iid": "hero_angel_valkyrie",
					"data": {
						"id": "angel_valkyrie",
						"level": 1,
						"ascension": 0,
						"evolution": 1,
						"skill": 1,
						"equipment": []
					},
					"type": "hero"
				},
				{
					"aid": self.accountId,
					"iid": "hero_human_desert_spear",
					"data": {
						"id": "human_desert_spear",
						"level": 1,
						"ascension": 0,
						"evolution": 1,
						"skill": 1,
						"equipment": []
					},
					"type": "hero"
				},
				{
					"aid": self.accountId,
					"iid": "hero_human_ba_knight",
					"data": {
						"id": "human_ba_knight",
						"level": 1,
						"ascension": 0,
						"evolution": 1,
						"skill": 1,
						"equipment": []
					},
					"type": "hero"
				},
				{
					"aid": self.accountId,
					"iid": "hero_elven_wind_mage",
					"data": {
						"id": "elven_wind_mage",
						"level": 1,
						"ascension": 0,
						"evolution": 1,
						"skill": 1,
						"equipment": []
					},
					"type": "hero"
				},
				{
					"aid": self.accountId,
					"iid": "hero_human_cleric",
					"data": {
						"id": "human_cleric",
						"level": 1,
						"ascension": 0,
						"evolution": 1,
						"skill": 1,
						"equipment": []
					},
					"type": "hero"
				},
				{
					"aid": self.accountId,
					"iid": "hero_ogre_brawler",
					"data": {
						"id": "ogre_brawler",
						"level": 1,
						"ascension": 2,
						"evolution": 2,
						"skill": 1,
						"equipment": []
					},
					"type": "hero"
				},
				{
					"aid": self.accountId,
					"iid": "hero_human_lancer",
					"data": {
						"id": "human_lancer",
						"level": 1,
						"ascension": 0,
						"evolution": 1,
						"skill": 1,
						"equipment": []
					},
					"type": "hero"
				},
				{
					"aid": self.accountId,
					"iid": "hero_orc_shrapnel",
					"data": {
						"id": "orc_shrapnel",
						"level": 1,
						"ascension": 0,
						"evolution": 1,
						"skill": 1,
						"equipment": []
					},
					"type": "hero"
				},
				{
					"aid": self.accountId,
					"iid": "hero_giant_forest_sage",
					"data": {
						"id": "giant_forest_sage",
						"level": 1,
						"ascension": 0,
						"evolution": 1,
						"skill": 1,
						"equipment": []
					},
					"type": "hero"
				},
				{
					"aid": self.accountId,
					"iid": "hero_orc_armored_defender",
					"data": {
						"id": "orc_armored_defender",
						"level": 1,
						"ascension": 0,
						"evolution": 1,
						"skill": 1,
						"equipment": []
					},
					"type": "hero"
				},
				{
					"aid": self.accountId,
					"iid": "hero_elven_phoenix",
					"data": {
						"id": "elven_phoenix",
						"level": 1,
						"ascension": 0,
						"evolution": 1,
						"skill": 1,
						"equipment": []
					},
					"type": "hero"
				},
				{
					"aid": self.accountId,
					"iid": "hero_human_archmage",
					"data": {
						"id": "human_archmage",
						"level": 1,
						"ascension": 0,
						"evolution": 1,
						"skill": 1,
						"equipment": []
					},
					"type": "hero"
				},
				{
					"aid": self.accountId,
					"iid": "levelRunInfo_campaign_02_03",
					"data": {
						"levelId": "campaign_02_03",
						"completionTime": 99,
						"stars": 3,
						"failedAttempts": 4,
						"attempts": 6
					},
					"type": "levelRunInfo"
				},
				{
					"aid": self.accountId,
					"iid": "levelRunInfo_campaign_02_04",
					"data": {
						"levelId": "campaign_02_04",
						"completionTime": -1,
						"stars": 3,
						"failedAttempts": 1,
						"attempts": 2
					},
					"type": "levelRunInfo"
				},
				{
					"aid": self.accountId,
					"iid": "levelRunInfo_dungeon_skill_ranged_01",
					"data": {
						"levelId": "dungeon_skill_ranged_01",
						"completionTime": -1,
						"stars": 0,
						"failedAttempts": 3,
						"attempts": 3
					},
					"type": "levelRunInfo"
				},
				{
					"aid": self.accountId,
					"iid": "hero_angel_breaker",
					"data": {
						"id": "angel_breaker",
						"level": 1,
						"ascension": 2,
						"evolution": 2,
						"skill": 1,
						"equipment": []
					},
					"type": "hero"
				},
				{
					"aid": self.accountId,
					"iid": "hero_angel_duelist",
					"data": {
						"id": "angel_duelist",
						"level": 1,
						"ascension": 0,
						"evolution": 1,
						"skill": 1,
						"equipment": []
					},
					"type": "hero"
				},
				{
					"aid": self.accountId,
					"iid": "hero_angel_grove_warden",
					"data": {
						"id": "angel_grove_warden",
						"level": 1,
						"ascension": 6,
						"evolution": 6,
						"skill": 1,
						"equipment": []
					},
					"type": "hero"
				},
				{
					"aid": self.accountId,
					"iid": "hero_demon_lavamancer",
					"data": {
						"id": "demon_lavamancer",
						"level": 1,
						"ascension": 0,
						"evolution": 1,
						"skill": 1,
						"equipment": []
					},
					"type": "hero"
				},
				{
					"aid": self.accountId,
					"iid": "hero_elven_gothic_defender",
					"data": {
						"id": "elven_gothic_defender",
						"level": 1,
						"ascension": 0,
						"evolution": 1,
						"skill": 1,
						"equipment": []
					},
					"type": "hero"
				},
				{
					"aid": self.accountId,
					"iid": "hero_elven_hydra",
					"data": {
						"id": "elven_hydra",
						"level": 1,
						"ascension": 0,
						"evolution": 1,
						"skill": 1,
						"equipment": []
					},
					"type": "hero"
				},
				{
					"aid": self.accountId,
					"iid": "hero_elven_magitech_archer",
					"data": {
						"id": "elven_magitech_archer",
						"level": 1,
						"ascension": 0,
						"evolution": 1,
						"skill": 1,
						"equipment": []
					},
					"type": "hero"
				},
				{
					"aid": self.accountId,
					"iid": "hero_elven_mender",
					"data": {
						"id": "elven_mender",
						"level": 1,
						"ascension": 0,
						"evolution": 1,
						"skill": 1,
						"equipment": []
					},
					"type": "hero"
				},
				{
					"aid": self.accountId,
					"iid": "hero_elven_swordsman",
					"data": {
						"id": "elven_swordsman",
						"level": 1,
						"ascension": 0,
						"evolution": 1,
						"skill": 1,
						"equipment": []
					},
					"type": "hero"
				},
				{
					"aid": self.accountId,
					"iid": "hero_giant_frost_defender",
					"data": {
						"id": "giant_frost_defender",
						"level": 1,
						"ascension": 0,
						"evolution": 1,
						"skill": 1,
						"equipment": []
					},
					"type": "hero"
				},
				{
					"aid": self.accountId,
					"iid": "hero_human_pharaoh",
					"data": {
						"id": "human_pharaoh",
						"level": 1,
						"ascension": 0,
						"evolution": 1,
						"skill": 1,
						"equipment": []
					},
					"type": "hero"
				},
				{
					"aid": self.accountId,
					"iid": "hero_human_reaper",
					"data": {
						"id": "human_reaper",
						"level": 1,
						"ascension": 0,
						"evolution": 1,
						"skill": 1,
						"equipment": []
					},
					"type": "hero"
				},
				{
					"aid": self.accountId,
					"iid": "hero_werewolf_frostpierce",
					"data": {
						"id": "werewolf_frostpierce",
						"level": 1,
						"ascension": 0,
						"evolution": 1,
						"skill": 1,
						"equipment": []
					},
					"type": "hero"
				},
				{
					"aid": self.accountId,
					"iid": "hero_werewolf_hammergod",
					"data": {
						"id": "werewolf_hammergod",
						"level": 1,
						"ascension": 0,
						"evolution": 1,
						"skill": 1,
						"equipment": []
					},
					"type": "hero"
				},
				{
					"aid": self.accountId,
					"iid": "hero_werewolf_justicar",
					"data": {
						"id": "werewolf_justicar",
						"level": 1,
						"ascension": 0,
						"evolution": 1,
						"skill": 1,
						"equipment": []
					},
					"type": "hero"
				},
				{
					"aid": self.accountId,
					"iid": "levelRunInfo_dungeon_skill_ranged_02",
					"data": {
						"levelId": "dungeon_skill_ranged_02",
						"completionTime": -1,
						"stars": 0,
						"failedAttempts": 1,
						"attempts": 1
					},
					"type": "levelRunInfo"
				},
				{
					"aid": self.accountId,
					"iid": "levelRunInfo_dungeon_skill_ranged_05",
					"data": {
						"levelId": "dungeon_skill_ranged_05",
						"completionTime": -1,
						"stars": 0,
						"failedAttempts": 1,
						"attempts": 1
					},
					"type": "levelRunInfo"
				},
				{
					"aid": self.accountId,
					"iid": "levelRunInfo_dungeon_skill_melee_01",
					"data": {
						"levelId": "dungeon_skill_melee_01",
						"completionTime": -1,
						"stars": 0,
						"failedAttempts": 1,
						"attempts": 1
					},
					"type": "levelRunInfo"
				},
				{
					"aid": self.accountId,
					"iid": "levelRunInfo_dungeon_skill_melee_04",
					"data": {
						"levelId": "dungeon_skill_melee_04",
						"completionTime": -1,
						"stars": 0,
						"failedAttempts": 1,
						"attempts": 1
					},
					"type": "levelRunInfo"
				},
				{
					"aid": self.accountId,
					"iid": "levelRunInfo_campaign_03_02",
					"data": {
						"levelId": "campaign_03_02",
						"completionTime": -1,
						"stars": 0,
						"failedAttempts": 5,
						"attempts": 5
					},
					"type": "levelRunInfo"
				},
				{
					"aid": self.accountId,
					"iid": "levelRunInfo_dungeon_gold_06",
					"data": {
						"levelId": "dungeon_gold_06",
						"completionTime": -1,
						"stars": 0,
						"failedAttempts": 4,
						"attempts": 4
					},
					"type": "levelRunInfo"
				},
				{
					"aid": self.accountId,
					"iid": "levelRunInfo_dungeon_gold_03",
					"data": {
						"levelId": "dungeon_gold_03",
						"completionTime": -1,
						"stars": 3,
						"failedAttempts": 6,
						"attempts": 7
					},
					"type": "levelRunInfo"
				},
				{
					"aid": self.accountId,
					"iid": "levelRunInfo_dungeon_gold_05",
					"data": {
						"levelId": "dungeon_gold_05",
						"completionTime": -1,
						"stars": 0,
						"failedAttempts": 1,
						"attempts": 1
					},
					"type": "levelRunInfo"
				},
				{
					"aid": self.accountId,
					"iid": "levelRunInfo_campaign_02_15",
					"data": {
						"levelId": "campaign_02_15",
						"completionTime": -1,
						"stars": 0,
						"failedAttempts": 2,
						"attempts": 2
					},
					"type": "levelRunInfo"
				},
				{
					"aid": self.accountId,
					"iid": "levelRunInfo_dungeon_exp_06",
					"data": {
						"levelId": "dungeon_exp_06",
						"completionTime": -1,
						"stars": 0,
						"failedAttempts": 1,
						"attempts": 1
					},
					"type": "levelRunInfo"
				}
			],
			"screenName": "Player19944066",
			"game": "57901c6df82a45708018ba73b8d16004",
			"secret": "72d0676767714480b1e4cec845105332"
		}, name = "/nuke")