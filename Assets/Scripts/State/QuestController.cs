using UnityEngine;

using System;
using System.Collections.Generic;
using System.Linq;

using Game.Behaviour;
using Game.Settings;
using Game.Common;
using Game.Common.Quests;

using Random = UnityEngine.Random;

namespace Game.State {
	public sealed class QuestController : BaseController {
		const int   OtherTweetsCount      = 10;
		const float QuestFinishDelay      = 2.0f;
		const float QuestFinishGlitchTime = 2.0f;
		const float QuestEventGlitchTime  = 0.5f;

		readonly TweetsController   _tweetsController;
		readonly GlitchController   _glitchController;
		readonly ProgressController _progressController;

		QuestCollection        _questCollection;
		SenderCollection       _senderCollection;
		TweetSpritesCollection _tweetSpritesCollection;

		QuestCollection.QuestInfo _upcomingQuestInfo;

		int   _questIndex;
		float _finishTimer;

		readonly List<BaseQuestEvent> _pendingQuestEvents = new List<BaseQuestEvent>();

		public event Action TweetsUpdated = () => {};
		public event Action GameFinish    = () => {};

		public event Action<int, Sprite> OnSenderAvatarChanged;
		public event Action<int, Sprite> OnTweetImageChanged;

		public Tweet[] CurrentTweets { get; private set; } = new Tweet[0];

		public QuestController(TweetsController tweetsController, GlitchController glitchController, ProgressController progressController) {
			_tweetsController   = tweetsController;
			_glitchController   = glitchController;
			_progressController = progressController;
		}

		public override void Init() {
			_questCollection        = Resources.Load<QuestCollection>("QuestCollection");
			_senderCollection       = Resources.Load<SenderCollection>("SenderCollection");
			_tweetSpritesCollection = Resources.Load<TweetSpritesCollection>("TweetSpritesCollection");
			SetupCurrentTweets();

			var questInfo = _questCollection.TryGetQuestInfo(_questIndex);
			if ( questInfo == null ) {
				Debug.LogError("No quests");
				return;
			}
			_pendingQuestEvents.AddRange(questInfo.QuestEvents);
			OnQuestStarted(_questIndex);
		}

		public override void Update() {
			if ( _upcomingQuestInfo == null ) {
				return;
			}
			_finishTimer += Time.deltaTime;
			if ( _finishTimer < QuestFinishDelay ) {
				return;
			}
			_finishTimer = 0;
			HandleQuestFinish(_upcomingQuestInfo);
			_upcomingQuestInfo = null;
		}

		public bool TryPost(string message) {
			var info = _questCollection.TryGetQuestInfo(_questIndex);
			if ( info == null ) {
				Debug.LogError("No more quests");
				return false;
			}
			if ( info.ReplyId > 0 ) {
				Debug.LogWarning("Answer should be posted as a reply");
				return false;
			}
			return TryHandleAnswer(info, message);
		}

		public bool TryReply(int tweetId, string message) {
			var info = _questCollection.TryGetQuestInfo(_questIndex);
			if ( info == null ) {
				Debug.LogError("No more quests");
				return false;
			}
			if ( info.ReplyId == 0 ) {
				Debug.LogWarning("Answer should be posted as a post");
				return false;
			}
			if ( tweetId != info.ReplyId ) {
				Debug.LogWarning("Answer should be posted as a reply to another post");
				return false;
			}
			return TryHandleAnswer(info, message);
		}

		public void OnImageShowFinished(int tweetId) {
			TryFireQuestEvents(trigger =>
				(trigger.Type == QuestEventTriggerType.ImageShowFinished) && (int.Parse(trigger.Arg) == tweetId));
		}

		public void OnCommentPosted(int parentTweetId) {
			TryFireQuestEvents(trigger =>
				(trigger.Type == QuestEventTriggerType.PlayerCommentPosted) &&
				(int.Parse(trigger.Arg) == parentTweetId));
		}

		void OnQuestStarted(int questIndex) {
			TryFireQuestEvents(trigger =>
				(trigger.Type == QuestEventTriggerType.QuestStarted) && (int.Parse(trigger.Arg) == questIndex));
		}

		void TryFireQuestEvents(Func<QuestEventTrigger, bool> triggerChecker) {
			if ( triggerChecker == null ) {
				Debug.LogError("TriggerChecker is null");
				return;
			}
			for ( var i = _pendingQuestEvents.Count - 1; i >= 0; i-- ) {
				var questEvent = _pendingQuestEvents[i];
				if ( triggerChecker(questEvent.Trigger) ) {
					FireEvent(questEvent);
					_pendingQuestEvents.RemoveAt(i);
				}
			}
		}

		void FireEvent(BaseQuestEvent baseQuestEvent) {
			_glitchController.AddConstantly(baseQuestEvent.BaseGlitchIncrease);
			_glitchController.AddOneShot(baseQuestEvent.OneShotGlitch, QuestEventGlitchTime);
			switch ( baseQuestEvent.Type ) {
				case QuestEventType.ChangeSenderAvatar when baseQuestEvent is ChangeSenderAvatarQuestEvent questEvent: {
					_senderCollection.SetOverrideSprite(questEvent.SenderId, questEvent.NewAvatar);
					OnSenderAvatarChanged?.Invoke(questEvent.SenderId, questEvent.NewAvatar);
					break;
				}
				case QuestEventType.SpawnComment when baseQuestEvent is SpawnCommentQuestEvent questEvent: {
					_tweetsController.AddComment(_tweetsController.GetTweetById(questEvent.ParentTweetId),
						_tweetsController.GetTweetById(questEvent.TweetId));
					break;
				}
				case QuestEventType.RemoveTweet when baseQuestEvent is RemoveTweetQuestEvent questEvent: {
					_tweetsController.RemoveTweet(_tweetsController.GetTweetById(questEvent.TweetId));
					SetupCurrentTweets();
					TweetsUpdated();
					break;
				}
				case QuestEventType.ChangeTweetImage when baseQuestEvent is ChangeTweetImageQuestEvent questEvent: {
					_tweetSpritesCollection.SetOverrideSprite(questEvent.TweetId, questEvent.NewImage);
					OnTweetImageChanged?.Invoke(questEvent.TweetId, questEvent.NewImage);
					break;
				}
				case QuestEventType.ChangeTweetMessage when baseQuestEvent is ChangeTweetMessageQuestEvent questEvent: {
					_tweetsController.ReplaceTweetMessage(questEvent.TweetId,
						_tweetsController.GetTweetById(questEvent.ReservedTweetId).Message);
					break;
				}
				default: {
					Debug.LogErrorFormat("Unsupported QuestEventType '{0}'", baseQuestEvent.Type.ToString());
					break;
				}
			}
		}

		bool TryHandleAnswer(QuestCollection.QuestInfo info, string message) {
			if ( info.CorrectAnswer == message.Trim() ) {
				Debug.Log("Answer is correct");
				_upcomingQuestInfo = info;
				return true;
			}
			Debug.LogWarning("Answer is not correct");
			return false;
		}

		void HandleQuestFinish(QuestCollection.QuestInfo info) {
			_progressController.FinishGame(_questIndex.ToString(), () => {});
			_questIndex++;

			if ( _pendingQuestEvents.Count > 0 ) {
				Debug.LogErrorFormat("Not all quest events fired");
				_pendingQuestEvents.Clear();
			}
			var questInfo = _questCollection.TryGetQuestInfo(_questIndex);
			if ( questInfo != null ) {
				_pendingQuestEvents.AddRange(questInfo.QuestEvents);
			} else {
				GameFinish();
			}

			SetupCurrentTweets();
			_glitchController.AddConstantly(info.BaseGlitchIncrease);
			_glitchController.AddOneShot(info.OneShotGlitch, QuestFinishGlitchTime);
			TweetsUpdated();

			if ( questInfo != null ) {
				OnQuestStarted(_questIndex);
			}
		}

		void SetupCurrentTweets() {
			if ( !Enum.TryParse<TweetType>("Quest" + _questIndex, out var questType) ) {
				return;
			}
			var currentTweets = _tweetsController.GetRootTweetsByType(
					new TweetType[] { questType, TweetType.Player },
					OtherTweetsCount, TweetType.Filler, TweetType.Generated)
				.ToArray();
			Shuffle(currentTweets);
			var firstTweet  = currentTweets.FirstOrDefault(t => t.Id == 1001);
			var secondTweet = currentTweets.FirstOrDefault(t => t.Id == 1000);
			var tmp = currentTweets.ToList();
			if ( firstTweet != null ) {
				tmp.Remove(firstTweet);
				tmp.Insert(0, firstTweet);
			}
			if ( secondTweet != null ) {
				tmp.Remove(secondTweet);
				tmp.Insert(1, secondTweet);
			}
			CurrentTweets = tmp.ToArray();
		}

		void Shuffle(Tweet[] tweets) {
			var count = tweets.Length;
			while ( count > 1 ) {
				--count;
				var rand = Random.Range(0, count + 1);
				var val  = tweets[rand];
				tweets[rand]  = tweets[count];
				tweets[count] = val;
			}
		}
	}
}