namespace Fusion.Addons.KCC
{
	using UnityEngine;

	/// <summary>
	/// This processor detect steps (obstacles which block the character moving forward) and reflects blocked movement upwards.
	/// </summary>
	public class StepUpProcessor : KCCProcessor, IAfterMoveStep
	{
		// CONSTANTS

		public static readonly int DefaultPriority = -1000;

		// PRIVATE MEMBERS

		[SerializeField][Tooltip("Maximum obstacle height to step on it.")]
		private float _stepHeight = 0.5f;
		[SerializeField][Tooltip("Maximum depth of the step check.")]
		private float _stepDepth = 0.2f;
		[SerializeField][Tooltip("Multiplier of unapplied movement projected to step up. This helps traversing obstacles faster.")]
		private float _stepSpeed = 1.0f;
		[SerializeField][Tooltip("Minimum proportional penetration push-back distance to activate step-up. A value of 0.5f means the KCC must be pushed back from colliding geometry by at least 50% of desired movement.")]
		[Range(0.25f, 0.75f)]
		private float _minPushBack = 0.5f;
		[SerializeField][Tooltip("Radius multiplier used for last sphere-cast (ground surface detection). Lower value work better with shorter step depth.")]
		[Range(0.25f, 1.0f)]
		private float _groundCheckRadiusScale = 0.5f;
		[SerializeField][Tooltip("Clears dynamic velocity when the step up is over. This eliminates bumps when dynamic up velocity is positive (for example after triggering jump).")]
		private bool  _clearDynamicVelocityOnEnd = true;
		[SerializeField][Tooltip("Step-up starts only if a walkable target surface (angle <= KCCData.MaxGroundAngle) is detected. Also cancels the step-up when no target surface is found at all.")]
		private bool  _requireGroundTarget = false;
		[SerializeField][Tooltip("Force extra update of collision hits if the step-up is active and moves the KCC.")]
		private bool  _forceUpdateHits = false;

		private KCCOverlapInfo   _overlapInfo   = new KCCOverlapInfo();
		private KCCShapeCastInfo _shapeCastInfo = new KCCShapeCastInfo();

		// StepUpProcessor INTERFACE

		protected virtual void OnStepUpBegin(KCC kcc, KCCData data) {}
		protected virtual void OnStepUpEnd(KCC kcc, KCCData data)   {}

		// KCCProcessor INTERFACE

		public override float GetPriority(KCC kcc) => DefaultPriority;

		// IAfterMoveStep INTERFACE

		public void Execute(AfterMoveStep stage, KCC kcc, KCCData data)
		{
			if (_stepHeight <= 0.0f || _stepDepth <= 0.0f || _stepSpeed <= 0.0f)
				return;

			// Ignore step-up after jump and teleport.
			if (data.JumpFrames > 0 || data.HasTeleported == true)
			{
				ProcessStepUpResult(kcc, data, false);
				return;
			}

			Vector3 checkDesiredDeltaXZ    = (data.DesiredPosition - data.BasePosition).OnlyXZ();
			float   checkDesiredDistanceXZ = checkDesiredDeltaXZ.magnitude;

			// No horizontal movement, stopping step-up.
			if (checkDesiredDistanceXZ < 0.001f)
			{
				ProcessStepUpResult(kcc, data, false);
				return;
			}

			bool tryStepUp = false;

			if (data.WasSteppingUp == true && HasCollisionsWithinExtent(stage.OverlapInfo, ECollisionType.Slope | ECollisionType.Wall | ECollisionType.Hang) == true)
			{
				tryStepUp = true;
			}
			else
			{
				// Following check compares desired distance with real distance traveled by KCC and triggers step-up
				// if something is pushing KCC back on horizontal plane, lowering the distance traveled by more than min push back.

				Vector3 targetDeltaXZ    = (data.TargetPosition - data.BasePosition).OnlyXZ();
				float   targetDistanceXZ = targetDeltaXZ.magnitude;

				if (targetDistanceXZ / checkDesiredDistanceXZ < _minPushBack)
				{
					tryStepUp = true;
				}
			}

			if (tryStepUp == false)
			{
				ProcessStepUpResult(kcc, data, false);
				return;
			}

			Vector3 basePosition    = data.BasePosition;
			Vector3 desiredPosition = data.DesiredPosition;
			Vector3 targetPosition  = data.TargetPosition;

			Vector3 desiredDelta     = desiredPosition - basePosition;
			Vector3 desiredDirection = Vector3.Normalize(desiredDelta);

			// The step-up is not triggered if there is no pending movement from the player or external sources.
			if (desiredDirection == default)
			{
				ProcessStepUpResult(kcc, data, false);
				return;
			}

			// Ignore step-up while moving upwards or downwards (with ~25° deviation).
			float desiredDirectionUpDot = Vector3.Dot(desiredDirection, Vector3.up);
			if (Mathf.Abs(desiredDirectionUpDot) >= 0.9f)
			{
				ProcessStepUpResult(kcc, data, false);
				return;
			}

			Vector3 correctionDelta     = targetPosition - desiredPosition;
			float   correctionDistance  = correctionDelta.magnitude;
			Vector3 correctionDirection = correctionDistance > 0.001f ? correctionDelta / correctionDistance : -desiredDirection;

			// The step-up is not triggered if the correction vector overlaps desired movement hemisphere.
			if (Vector3.Dot(desiredDirection, correctionDirection) >= 0.0f)
			{
				ProcessStepUpResult(kcc, data, false);
				return;
			}

			Vector3 desiredCheckDirectionXZ    = Vector3.Normalize(desiredDirection.OnlyXZ());
			Vector3 correctionCheckDirectionXZ = Vector3.Normalize(-correctionDirection.OnlyXZ());
			Vector3 combinedCheckDirectionXZ   = Vector3.Normalize(desiredCheckDirectionXZ + correctionCheckDirectionXZ);

			// Additional XZ comparison of desired direction and correction direction with deviation of ~85°.
			if (Vector3.Dot(desiredCheckDirectionXZ, correctionCheckDirectionXZ) < 0.1f)
			{
				ProcessStepUpResult(kcc, data, false);
				return;
			}

			// Following image shows movement step and collision with a geometry from top-down perspective.
			// P = Player position before movement.
			// D = Desired position before depenetration.
			// T = Target position after depenetration.
			// I = Impact position (intersection of P->D and collider plane).
			//
			//         D
			//         | \
			// --------T---I------- <- Obstacle
			//              \
			//                P
			//
			// Following code recalculates base step-up position from target position (T) to impact position (I).
			// This eliminates sliding along the collider when stepping up.
			if (correctionDirection.IsZero() == false && HasCollisionsWithinExtent(stage.OverlapInfo, ECollisionType.Slope) == false)
			{
				Ray   ray   = new Ray(basePosition - desiredDelta * 2.0f, desiredDirection);
				Plane plane = new Plane(correctionDirection, targetPosition);

				if (plane.Raycast(ray, out float distance) == true)
				{
					targetPosition = ray.GetPoint(distance);
				}
			}

			float   overlapRadius        = kcc.Settings.Radius - kcc.Settings.Extent;
			Vector3 upCheckPosition      = targetPosition + new Vector3(0.0f, _stepHeight, 0.0f);
			Vector3 forwardCheckPosition = upCheckPosition + combinedCheckDirectionXZ * _stepDepth;
			Vector3 groundCheckPosition  = forwardCheckPosition;
			float   groundCheckRadius    = overlapRadius;

			if (_groundCheckRadiusScale < 1.0f)
			{
				// Ground check can be done with smaller radius to compensate normal on edges.
				groundCheckRadius    = kcc.Settings.Radius * _groundCheckRadiusScale;
				groundCheckPosition += combinedCheckDirectionXZ * (kcc.Settings.Radius - kcc.Settings.Extent - groundCheckRadius);
			}

			float maxStepHeight        = _stepHeight;
			bool  highestPointFound    = default;
			bool  groundTargetFound    = default;
			bool  reachableTargetFound = default;

			// 1. Downward collision check to get real step height.
			// This check is executed before clearance checks because their position depends on the detected step height.
			if (kcc.SphereCast(_shapeCastInfo, groundCheckPosition + new Vector3(0.0f, kcc.Settings.Radius, 0.0f), groundCheckRadius, Vector3.down, _stepHeight + kcc.Settings.Radius, QueryTriggerInteraction.Ignore, false) == true)
			{
				float   minGroundDot = Mathf.Cos(Mathf.Clamp(data.MaxGroundAngle, 0.0f, 90.0f) * Mathf.Deg2Rad);
				Vector3 highestPoint = new Vector3(0.0f, float.MinValue, 0.0f);

				// Surfaces only slightly above the base position are not step targets. The bottom sphere of the capsule rides over them on its own,
				// the resulting contact is classified as ground and no step-up is needed. Comparing against the bare base position instead lets the
				// floor the character is already standing on pass on rounding noise, which starts a zero height step-up. That latches KCCData.IsSteppingUp
				// and every following tick then skips the ground target check, so the KCC keeps lifting with nothing to stand on.
				// The capsule rides over obstacles up to Radius * (1 - Cos(MaxGroundAngle)), which is half the radius at the default 60 degrees.
				float minStepTargetHeight = targetPosition.y + kcc.Settings.Radius / 3.0f;

				for (int i = 0, count = _shapeCastInfo.ColliderHitCount; i < count; ++i)
				{
					RaycastHit raycastHit      = _shapeCastInfo.ColliderHits[i].RaycastHit;
					Vector3    raycastHitPoint = raycastHit.point;

					if (raycastHitPoint.y <= minStepTargetHeight)
						continue;

					// Every surface above the base position defines how high the character has to be lifted to clear it, walkable or not.
					// Sizing the step-up from the highest one is what lifts the KCC over the next riser on stairs with a shallow tread.
					if (raycastHitPoint.y > highestPoint.y)
					{
						highestPoint      = raycastHitPoint;
						highestPointFound = true;
					}

					// Only surfaces flatter than KCCData.MaxGroundAngle are valid step targets, the character cannot stand on the others.
					// A riser hit alone must not enable the step-up, but it also must not cancel it when a walkable tread is present.
					if (Vector3.Dot(raycastHit.normal, Vector3.up) >= minGroundDot)
					{
						groundTargetFound = true;
					}

					// Surfaces above the step height cannot be stood on, but a lower surface detected by the same check still can.
					if (raycastHitPoint.y <= targetPosition.y + _stepHeight)
					{
						reachableTargetFound = true;
					}
				}

				if (highestPointFound == true)
				{
					maxStepHeight = Mathf.Clamp(highestPoint.y - targetPosition.y, 0.0f, _stepHeight);
				}
			}

			// KCCSettings step height is a hard limit. Without this check the KCC is lifted to the step height and the remaining lip is then
			// traversed by penetration resolution, which makes the effective maximum step height larger than configured. On obstacles just above
			// the limit the KCC repeatedly lifts, fails to reach the top and falls back, which is visible as jitter.
			// The check requires a reachable surface to exist, it does not require the highest one to be reachable - on stairs with a shallow tread
			// the ground check also detects the tread above the target one and that higher hit is what sizes the lift over the next riser.
			if (highestPointFound == true && reachableTargetFound == false)
			{
				ProcessStepUpResult(kcc, data, false);
				return;
			}

			// For initial attempt, do not try to step up without a walkable target surface.
			if (_requireGroundTarget == true && data.IsSteppingUp == false && data.WasSteppingUp == false && groundTargetFound == false)
			{
				ProcessStepUpResult(kcc, data, false);
				return;
			}

			// Clearance checks are executed at the real step height instead of the maximum step height.
			// Otherwise geometry above the step (door frames, lights) incorrectly cancels the step up
			// and increasing the step height makes the character refuse steps it can physically climb.
			if (highestPointFound == true)
			{
				// The margin only has to lift the capsule off the detected surface so the overlap does not report an exact touching contact,
				// which is unreliable in PhysX. Using KCCSettings.Extent here made the clearance requirement stricter than the KCC itself -
				// the character can stand on the step but the step-up refuses to enter, by exactly Extent.
				float clearanceHeight = Mathf.Min(maxStepHeight + 0.001f, _stepHeight);

				upCheckPosition.y      = targetPosition.y + clearanceHeight;
				forwardCheckPosition.y = targetPosition.y + clearanceHeight;
			}

			// 2. Upward collision check.
			if (kcc.CapsuleOverlap(_overlapInfo, upCheckPosition, overlapRadius, kcc.Settings.Height, QueryTriggerInteraction.Ignore) == true)
			{
				ProcessStepUpResult(kcc, data, false);
				return;
			}

			// 3. Forward collision check. Forward = Combination of desired XZ direction and negative correction XZ direction - to eliminate stepping up along collider surface.
			if (kcc.CapsuleOverlap(_overlapInfo, forwardCheckPosition, overlapRadius, kcc.Settings.Height, QueryTriggerInteraction.Ignore) == true)
			{
				ProcessStepUpResult(kcc, data, false);
				return;
			}

			// Project unapplied movement as step-up movement.
			float desiredDistance   = Vector3.Distance(basePosition, desiredPosition);
			float traveledDistance  = Vector3.Distance(basePosition, targetPosition);
			float remainingDistance = Mathf.Clamp((desiredDistance - traveledDistance) * _stepSpeed, 0.0f, maxStepHeight);

			remainingDistance *= Mathf.Clamp01(Vector3.Dot(desiredDirection, -correctionDirection));

			data.TargetPosition = targetPosition + new Vector3(0.0f, remainingDistance, 0.0f);

			// KCC remains grounded state while stepping up.
			data.IsGrounded     = true;
			data.GroundNormal   = Vector3.up;
			data.GroundDistance = kcc.Settings.Extent;
			data.GroundPosition = data.TargetPosition;
			data.GroundTangent  = data.TransformDirection;

			if (_forceUpdateHits == true)
			{
				// New position is set, refresh collision hits after the stage.
				stage.RequestUpdateHits(true);
			}

			ProcessStepUpResult(kcc, data, true);
		}

		// PRIVATE METHODS

		private void ProcessStepUpResult(KCC kcc, KCCData data, bool isSteppingUp)
		{
			data.IsSteppingUp = isSteppingUp;

			if (data.IsSteppingUp == true && data.WasSteppingUp == false)
			{
				if (data.DynamicVelocity.y < 0.0f)
				{
					// Reset dynamic velocity Y axis when step-up is triggered.
					data.DynamicVelocity.y = 0.0f;
				}

				OnStepUpBegin(kcc, data);
			}
			else if (data.IsSteppingUp == false && data.WasSteppingUp == true)
			{
				if (_clearDynamicVelocityOnEnd == true)
				{
					// Clearing dynamic velocity ensures that the movement from external sources and jump won't continue after hiting the edge.
					data.DynamicVelocity = default;
				}

				OnStepUpEnd(kcc, data);
			}
		}

		private static bool HasCollisionsWithinExtent(KCCOverlapInfo overlapInfo, ECollisionType collisionTypes)
		{
			for (int i = 0; i < overlapInfo.ColliderHitCount; ++i)
			{
				KCCOverlapHit hit = overlapInfo.ColliderHits[i];
				if (hit.IsWithinExtent == true && (collisionTypes & hit.CollisionType) != default)
					return true;
			}

			return false;
		}
	}
}
