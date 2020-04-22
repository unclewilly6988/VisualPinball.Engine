﻿using System;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;
using VisualPinball.Engine.Math;
using VisualPinball.Engine.Physics;
using VisualPinball.Unity.Extensions;
using VisualPinball.Unity.Physics.Collision;
using VisualPinball.Unity.VPT;
using VisualPinball.Unity.VPT.Ball;

namespace VisualPinball.Unity.Physics.Collider
{
	public struct CircleCollider : ICollider, ICollidable
	{
		private ColliderHeader _header;

		public float2 Center;
		public float Radius;

		private float _zHigh;
		private float _zLow;

		public ColliderType Type => _header.Type;

		public static void Create(BlobBuilder builder, HitCircle src, ref BlobPtr<Collider> dest)
		{
			ref var ptr = ref UnsafeUtilityEx.As<BlobPtr<Collider>, BlobPtr<CircleCollider>>(ref dest);
			ref var collider = ref builder.Allocate(ref ptr);
			collider.Init(src);
		}

		public static CircleCollider Create(HitCircle src)
		{
			var collider = default(CircleCollider);
			collider.Init(src);
			return collider;
		}

		private void Init(HitCircle src)
		{
			_header.Type = ColliderType.Circle;
			_header.ItemType = Collider.GetItemType(src.ObjType);
			_header.Id = src.Id;
			_header.Entity = new Entity {Index = src.ItemIndex, Version = src.ItemVersion};

			Center = src.Center.ToUnityFloat2();
			Radius = src.Radius;

			_zHigh = src.HitBBox.ZHigh;
			_zLow = src.HitBBox.ZLow;
		}

		public float HitTest(ref CollisionEventData coll, ref DynamicBuffer<BallInsideOfBufferElement> insideOfs, in BallData ball, float dTime)
		{
			// normal face, lateral, rigid
			return HitTestBasicRadius(ref coll, ref insideOfs, ball, dTime, true, true, true);
		}

		public float HitTestBasicRadius(ref CollisionEventData coll, ref DynamicBuffer<BallInsideOfBufferElement> insideOfs, in BallData ball, float dTime, bool direction, bool lateral, bool rigid)
		{
			// todo
			// if (!IsEnabled || ball.State.IsFrozen) {
			// 	return -1.0f;
			// }

			var c = new float3(Center.x, Center.y, 0.0f);
			var dist = ball.Position - c; // relative ball position
			var dv = ball.Velocity;

			var capsule3D = !lateral && ball.Position.z > _zHigh;
			var isKicker = _header.ItemType == ItemType.Kicker;
			var isKickerOrTrigger = _header.ItemType == ItemType.Trigger || _header.ItemType == ItemType.Kicker;

			float targetRadius;
			if (capsule3D) {
				targetRadius = Radius * (float) (13.0 / 5.0);
				c.z = _zHigh - Radius * (float) (12.0 / 5.0);
				dist.z = ball.Position.z - c.z; // ball rolling point - capsule center height
			}
			else {
				targetRadius = Radius;
				if (lateral) {
					targetRadius += ball.Radius;
				}

				dist.z = 0.0f;
				dv.z = 0.0f;
			}

			var bcddsq = math.lengthsq(dist);        // ball center to circle center distance ... squared
			var bcdd = math.sqrt(bcddsq);       // distance center to center
			if (bcdd <= 1.0e-6) {
				// no hit on exact center
				return -1.0f;
			}

			var b = math.dot(dist, dv);
			var bnv = b / bcdd;                  // ball normal velocity

			if (direction && bnv > PhysicsConstants.LowNormVel) {
				// clearly receding from radius
				return -1.0f;
			}

			var bnd = bcdd - targetRadius;       // ball normal distance to

			var a = math.lengthsq(dv);

			var hitTime = 0f;
			var isUnhit = false;
			var isContact = false;

			// Kicker is special.. handle ball stalled on kicker, commonly hit while receding, knocking back into kicker pocket
			if (isKicker && bnd <= 0 && bnd >= -Radius && a < PhysicsConstants.ContactVel * PhysicsConstants.ContactVel/* && ball.Hit.IsRealBall()*/) {
				BallData.SetOutsideOf(ref insideOfs, ref _header.Entity);
			}

			// contact positive possible in future ... objects Negative in contact now
			if (rigid && bnd < PhysicsConstants.PhysTouch) {
				if (bnd < -ball.Radius) {
					return -1.0f;
				}

				if (math.abs(bnv) <= PhysicsConstants.ContactVel) {
					isContact = true;

				} else {
					// estimate based on distance and speed along distance
					// the ball can be that fast that in the next hit cycle the ball will be inside the hit shape of a bumper or other element.
					// if that happens bnd is negative and greater than the negative bnv value that results in a negative hittime
					// below the "if (infNan(hittime) || hittime <0.F...)" will then be true and the hit function will return -1.0f = no hit
					hitTime = math.max(0.0f, (float) (-bnd / bnv));
				}

			} else if (isKickerOrTrigger /*&& ball.Hit.IsRealBall()*/ && bnd < 0 == BallData.IsOutsideOf(ref insideOfs, ref _header.Entity)) {
				// triggers & kickers

				// here if ... ball inside and no hit set .... or ... ball outside and hit set
				if (math.abs(bnd - Radius) < 0.05) {
					// if ball appears in center of trigger, then assumed it was gen"ed there
					BallData.SetInsideOf(ref insideOfs, ref _header.Entity); // special case for trigger overlaying a kicker

				} else {
					// this will add the ball to the trigger space without a Hit
					isUnhit = bnd > 0; // ball on outside is UnHit, otherwise it"s a Hit
				}

			} else {
				if (!rigid && bnd * bnv > 0 || a < 1.0e-8) {
					// (outside and receding) or (inside and approaching)
					// no hit ... ball not moving relative to object
					return -1.0f;
				}

				var sol = Functions.SolveQuadraticEq(a, 2.0f * b, bcddsq - targetRadius * targetRadius);
				if (sol == null) {
					return -1.0f;
				}

				var (time1, time2) = sol;
				isUnhit = time1 * time2 < 0;
				hitTime = isUnhit ? MathF.Max(time1, time2) : MathF.Min(time1, time2); // ball is inside the circle
			}

			if (float.IsNaN(hitTime) || float.IsInfinity(hitTime) || hitTime < 0 || hitTime > dTime) {
				// contact out of physics frame
				return -1.0f;
			}

			var hitZ = ball.Position.z + ball.Velocity.z * hitTime; // rolling point
			if (hitZ + ball.Radius * 0.5 < _zLow
			    || !capsule3D && hitZ - ball.Radius * 0.5 > _zHigh
			    || capsule3D && hitZ < _zHigh) {
				return -1.0f;
			}

			var hitX = ball.Position.x + ball.Velocity.x * hitTime;
			var hitY = ball.Position.y + ball.Velocity.y * hitTime;
			var sqrLen = (hitX - c.x) * (hitX - c.x) + (hitY - c.y) * (hitY - c.y);

			coll.HitNormal.x = 0;
			coll.HitNormal.y = 0;
			coll.HitNormal.z = 0;

			// over center?
			if (sqrLen > 1.0e-8) {
				// no
				var invLen = 1.0f / MathF.Sqrt(sqrLen);
				coll.HitNormal.x = (hitX - c.x) * invLen;
				coll.HitNormal.y = (hitY - c.y) * invLen;

			} else {
				// yes, over center
				coll.HitNormal.x = 0.0f; // make up a value, any direction is ok
				coll.HitNormal.y = 1.0f;
				coll.HitNormal.z = 0.0f;
			}

			if (!rigid) {
				// non rigid body collision? return direction
				coll.HitFlag = isUnhit; // UnHit signal is receding from target
			}

			coll.IsContact = isContact;
			if (isContact) {
				coll.HitOrgNormalVelocity = bnv;
			}

			coll.HitDistance = bnd; // actual contact distance ...
			//coll.M_hitRigid = rigid;                         // collision type

			return hitTime;
		}
	}
}