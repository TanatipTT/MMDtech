﻿using System;
using System.Collections.Generic;
using System.Linq;

#if UNITY_EDITOR
using UnityEditor;
#endif

using UnityEngine;
using UnityEngine.AI;
using UnityEngine.UI;
using Object = UnityEngine.Object;
using Random = UnityEngine.Random;

namespace GercStudio.USK.Scripts
{
	public static class AIHelper
	{

		public enum EnemyStates
		{
			Waypoints,
			Warning,
			Attack,
			Cover,
			FindAfterAttack
		}

		public enum AttackTypes
		{
			Bullets,
			Rockets,
			Fire,
			Melee
		}
		
		[Serializable]
		public class EnemyAttack
		{
			public AttackTypes AttackType;
			[Range(1, 100)] public int Damage = 5;
			[Range(0.1f, 2)] public float Scatter = 1;
			[Range(0.1f, 10)] public float RateOfAttack = 0.5f;
			public float InventoryAmmo = 20;
			public float CurrentAmmo = 20;
			
			public GameObject Rocket;
			public GameObject Fire;
			public GameObject MuzzleFlash;
			public GameObject Explosion;
			
			public List<Transform> AttackSpawnPoints;

			public AudioClip AttackAudio;
			
			public List<AnimationClip> MeleeAttackAnimations;
			public AnimationClip HandsAttackAnimation;
			public AnimationClip HandsIdleAnimation;
			public AnimationClip HandsReloadAnimation;

			public List<BoxCollider> DamageColliders;

			public bool UseReload;
		}

		[Serializable]
		public class Player
		{
			public GameObject player;
			public bool HearPlayer;
			public bool SeePlayer;
			public float warningValue;
			public float attackValue;
			public float distanceBetween;
			public float hearTime;
		}

		public static bool CheckRaycast(Transform targetPoint, Transform currentPoint, float horizontalAngleToSee, float heightToSee, float distanceToSee, bool attack, bool inGrass)
		{
			var direction = targetPoint.position - currentPoint.position;
			var look = Quaternion.LookRotation(direction);

			var horizontalAngle = look.eulerAngles.y;
			if (horizontalAngle > 180)
				horizontalAngle -= 360;

			var spineAngleY = currentPoint.eulerAngles.y;
			if (spineAngleY > 180)
				spineAngleY -= 360;

			var middleAngleY = Mathf.DeltaAngle(horizontalAngle, spineAngleY);

			var verticalAngle = look.eulerAngles.x;
			if (verticalAngle > 180)
				verticalAngle -= 360;

			var spineAngleX = currentPoint.eulerAngles.x;
			if (spineAngleX > 180)
				spineAngleX -= 360;

			var middleAngleX = Mathf.DeltaAngle(verticalAngle, spineAngleX);

			RaycastHit info;

			var layerMask = !attack ? ~ LayerMask.GetMask("Enemy") : ~ (LayerMask.GetMask("Enemy") | LayerMask.GetMask("Grass"));
			
			Physics.Linecast(currentPoint.position, targetPoint.position, out info, layerMask);

			var obstacle = false;

			if (info.transform)
			{
				if (!info.transform.root.gameObject.GetComponent<Controller>() && !info.transform.root.GetComponent<EnemyController>()
				                                                               && (!info.transform.root.GetComponent<Surface>() || info.transform.root.GetComponent<Surface>() && !info.transform.root.GetComponent<Surface>().Cover))
				{
					obstacle = true;
				}
					
			}

			if (inGrass && !attack)
			{
				if (Vector3.Distance(targetPoint.position, currentPoint.position) > 5)
				{
					obstacle = true;
				}
			}

// problem here			
			return Mathf.Abs(middleAngleY) < horizontalAngleToSee / 2 && Mathf.Abs(middleAngleX) < Mathf.Abs(Mathf.Asin(heightToSee / 2 / distanceToSee) * 180 / Mathf.PI) && !obstacle;
		}

		public static int GetNearestPoint(List<MovementBehavior.Behavior> points, Vector3 myPosition, int nextBehaviour, int lastBehaviour)
		{
			var bestPointIndex = 0;
			var closestDistanceSqr = Mathf.Infinity;
			for (var i = 0; i < points.Count; i++)
			{
				Vector3 directionToObject = points[i].point.transform.position - myPosition;
				var dSqrToTarget = directionToObject.sqrMagnitude;

				if (dSqrToTarget < closestDistanceSqr & i != nextBehaviour & i != lastBehaviour)
				{
					closestDistanceSqr = dSqrToTarget;
					bestPointIndex = i;
				}
			}


			return bestPointIndex;
		}

		public static Transform GetCoverPoint(EnemyController script)
		{
			Transform coverPoint = null;
			var collidersNearEnemy = Physics.OverlapSphere(script.transform.position, script.DistanceToSee * script.AttackDistancePercent / 100);
			var collidersNearPlayer = Physics.OverlapSphere(script.Players[0].player.transform.position, script.DistanceToSee * script.AttackDistancePercent / 100);

			var coversNearEnemy = new List<GameObject>();
			var coversNearPlayer = new List<GameObject>();
			
			var allEnemies = new List<EnemyController>(GameObject.FindObjectsOfType<EnemyController>());
			
			allEnemies.Remove(allEnemies.Find(enemy => enemy == script));
			allEnemies.RemoveAll(move =>	!move.currentCover);
			
			foreach (var collider in coversNearPlayer)
			{
				if (collider.gameObject.GetComponent<Surface>() && collider.gameObject.GetComponent<Surface>().Cover)
				{
					coversNearPlayer.Add(collider.gameObject);
				}
			}

			foreach (var collider in collidersNearEnemy)
			{
				if (collider.gameObject.GetComponent<Surface>() && collider.gameObject.GetComponent<Surface>().Cover)
				{
					if (collidersNearPlayer.Any(col => col.gameObject.GetInstanceID() == collider.gameObject.GetInstanceID()))
					{
						if (allEnemies.Count > 0)
						{
							if (allEnemies.All(enemyController => enemyController.currentCover.gameObject.GetInstanceID() != collider.gameObject.GetInstanceID()))
								coversNearEnemy.Add(collider.gameObject);
						}
						else
						{
							coversNearEnemy.Add(collider.gameObject);
						}
					}
				}
			}

			script.currentCover = FindClosestObject(coversNearEnemy.ToArray(), script);

			if (!script.currentCover)
				script.currentCover = FindClosestObject(coversNearPlayer.ToArray(), script);

			var newPoint = Vector3.zero;

			if (script.currentCover)
			{
				script.currentCoverDirection = script.currentCover.transform.position - script.Players[0].player.transform.position;
				script.currentCoverDirection.Normalize();

				newPoint = script.currentCover.transform.position;

				var i = 0f;
				while (script.currentCover.GetComponent<Collider>().bounds.Contains(newPoint - script.currentCoverDirection))
				{
					i += 0.5f;
					newPoint = script.currentCover.transform.position + script.currentCoverDirection * i;
				}

				coverPoint = new GameObject("Cover Point").transform;
				coverPoint.transform.position = newPoint;

#if UNITY_EDITOR
				Helper.AddObjectIcon(coverPoint.gameObject, "Cover Point");
#endif
			}

			if(coverPoint)
				coverPoint.tag = "CoverPoint";
			
//			if (coverPoint)
//				coverPoint.gameObject.hideFlags = HideFlags.HideInHierarchy;

			return coverPoint;
		}
		
#if UNITY_EDITOR
		public static void CreateNewStateCanvas(EnemyController enemyScript, Transform parent)
		{
			enemyScript.StateCanvas = Helper.NewCanvas("State Canvas", parent);
			enemyScript.StateCanvas.GetComponent<RectTransform>().sizeDelta = new Vector2(1, 1);

			enemyScript.backgroundImg = Helper.NewImage("Background", enemyScript.StateCanvas.transform, Vector2.one, Vector2.zero);
			enemyScript.backgroundImg.sprite = AssetDatabase.LoadAssetAtPath("Assets/Universal Shooter Kit/Tools/Assets/_Images/State Background.png", typeof(Sprite)) as Sprite;
			enemyScript.backgroundImg.raycastTarget = false;
			enemyScript.backgroundImg.type = Image.Type.Filled;
			enemyScript.backgroundImg.fillMethod = Image.FillMethod.Vertical;

			enemyScript.yellowImg = Helper.NewImage("Warning State", enemyScript.StateCanvas.transform, Vector2.one, Vector2.zero);
			enemyScript.yellowImg.sprite = AssetDatabase.LoadAssetAtPath("Assets/Universal Shooter Kit/Tools/Assets/_Images/State Warning.png", typeof(Sprite)) as Sprite;
			enemyScript.yellowImg.raycastTarget = false;
			enemyScript.yellowImg.type = Image.Type.Filled;
			enemyScript.yellowImg.fillMethod = Image.FillMethod.Vertical;


			enemyScript.redImg = Helper.NewImage("Attack State", enemyScript.StateCanvas.transform, Vector2.one, Vector2.zero);
			enemyScript.redImg.sprite = AssetDatabase.LoadAssetAtPath("Assets/Universal Shooter Kit/Tools/Assets/_Images/State Attack.png", typeof(Sprite)) as Sprite;
			enemyScript.redImg.raycastTarget = false;
			enemyScript.redImg.type = Image.Type.Filled;
			enemyScript.redImg.fillMethod = Image.FillMethod.Vertical;

			Helper.SetAndStretchToParentSize(enemyScript.backgroundImg.GetComponent<RectTransform>(), enemyScript.StateCanvas.GetComponent<RectTransform>(), false);
			Helper.SetAndStretchToParentSize(enemyScript.yellowImg.GetComponent<RectTransform>(), enemyScript.StateCanvas.GetComponent<RectTransform>(), false);
			Helper.SetAndStretchToParentSize(enemyScript.redImg.GetComponent<RectTransform>(), enemyScript.StateCanvas.GetComponent<RectTransform>(), false);

			enemyScript.StateCanvas.GetComponent<RectTransform>().anchoredPosition3D = new Vector3(0, 3, 0);
		}


		public static void CreateNewHealthCanvas(EnemyController enemyScript, Transform parent)
		{
			enemyScript.HealthCanvas = Helper.NewCanvas("Health Canvas", parent);
			enemyScript.HealthCanvas.GetComponent<RectTransform>().sizeDelta = new Vector2(1, 1);

			enemyScript.healthBarBackground = Helper.NewImage("Background", enemyScript.HealthCanvas.transform, Vector2.one, Vector2.zero);
			enemyScript.healthBarBackground.sprite = AssetDatabase.LoadAssetAtPath("Assets/Universal Shooter Kit/Tools/Assets/_Images/Health Bar Background.png", typeof(Sprite)) as Sprite;
			enemyScript.healthBarBackground.raycastTarget = false;
			enemyScript.healthBarBackground.type = Image.Type.Filled;
			enemyScript.healthBarBackground.fillMethod = Image.FillMethod.Horizontal;
			
			enemyScript.healthBarValue = Helper.NewImage("Value", enemyScript.HealthCanvas.transform, Vector2.one, Vector2.zero);
			enemyScript.healthBarValue.sprite = AssetDatabase.LoadAssetAtPath("Assets/Universal Shooter Kit/Tools/Assets/_Images/Health Bar Value.png", typeof(Sprite)) as Sprite;
			enemyScript.healthBarValue.raycastTarget = false;
			enemyScript.healthBarValue.type = Image.Type.Filled;
			enemyScript.healthBarValue.fillMethod = Image.FillMethod.Horizontal;
			enemyScript.healthBarValue.fillOrigin = 1;
			
			
			Helper.SetAndStretchToParentSize(enemyScript.healthBarBackground.GetComponent<RectTransform>(), enemyScript.HealthCanvas.GetComponent<RectTransform>(), false);
			Helper.SetAndStretchToParentSize(enemyScript.healthBarValue.GetComponent<RectTransform>(), enemyScript.HealthCanvas.GetComponent<RectTransform>(), false);
			
			enemyScript.HealthCanvas.GetComponent<RectTransform>().anchoredPosition3D = new Vector3(0, 3, 0);
		}
#endif

		static GameObject FindClosestObject(GameObject[] objects, EnemyController script)
		{
			GameObject closest = null;
			var distance = float.MaxValue;

			foreach (var obj in objects)
			{
				var enemyDistance = Vector3.Distance(obj.transform.position, script.transform.position);
				var characterDistance = Vector3.Distance(obj.transform.position, script.Players[0].player.transform.position);

				if (characterDistance > 10 && enemyDistance < distance)
				{
					closest = obj;
					distance = enemyDistance;
				}
			}

			return closest;
		}

		public static void AllSidesMovement(float angle, Animator anim)
		{
			NullDirectionAnimations(anim);

			if (angle > -23 && angle <= 23)
			{
				anim.SetBool("Forward", true);
			}
			else if (angle > 23 && angle <= 68)
			{
				anim.SetBool("ForwardRight", true);
			}
			else if (angle > 68 && angle <= 113)
			{
				anim.SetBool("Right", true);
			}
			else if (angle > 113 && angle <= 158)
			{
				anim.SetBool("BackwardRight", true);
			}
			else if (angle <= -23 && angle > -68)
			{
				anim.SetBool("ForwardLeft", true);
			}
			else if (angle <= -68 && angle > -113)
			{
				anim.SetBool("Left", true);
			}
			else if (angle <= -113 && angle > -158)
			{
				anim.SetBool("BackwardLeft", true);
			}
			else
			{
				anim.SetBool("Backward", true);
			}
		}

		static void NullDirectionAnimations(Animator anim)
		{
			anim.SetBool("Forward", false);
			anim.SetBool("ForwardRight", false);
			anim.SetBool("ForwardLeft", false);
			anim.SetBool("Left", false);
			anim.SetBool("Right", false);
			anim.SetBool("BackwardLeft", false);
			anim.SetBool("BackwardRight", false);
			anim.SetBool("Backward", false);
		}
		
		public static Transform CreatePointToCheck(Vector3 position, string type)
		{
			var point = new GameObject("Point to check");
			point.transform.position = position;
#if UNITY_EDITOR
			Helper.AddObjectIcon(point, type == "warning" ? "Point to check (warning)" : "Point to check (attack)");
#endif
			return point.transform;
		}
		
		public static Transform GeneratePointOnNavMesh(Vector3 position, Vector3 direction, float distance, ref float functionCount, bool randomNextDirection)
		{
			Transform point = null;
			
			functionCount++;
			direction.Normalize();

			var finalPosition = position + direction * distance;// + Vector3.up;
			//var hitColliders = Physics.OverlapSphere(finalPosition, 1);

			NavMeshHit hit;
            
			var onNavMesh = false;

			if (NavMesh.SamplePosition(finalPosition, out hit, 1000, NavMesh.AllAreas))
			{
				if (hit.distance < 3)
					onNavMesh = true;
			}

//			var touchAnyObject = false;
//
//			foreach (var collider in hitColliders)
//			{
//				if (collider)
//				{
//					if (!collider.transform.root.gameObject.GetComponent<EnemyController>())
//						touchAnyObject = true;
//				}
//			}

			if (onNavMesh)// && !touchAnyObject)
			{
				point = CreatePointToCheck(finalPosition, "warning");
			}
			else
			{
				if (functionCount < 3)
				{
					if (randomNextDirection)
						direction = new Vector3(Random.Range(-5, 5), 1, Random.Range(-5, 5));
					else
						distance /= 2;
					
					point = GeneratePointOnNavMesh(position, direction, distance, ref functionCount, randomNextDirection);
				}
			}
			
			if(point)
				point.position = new Vector3(point.position.x, position.y, point.position.z);
			
			return point;
		}
	}
}

