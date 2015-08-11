﻿/*
Copyright 2015, Boris-Barboris

This file is part of Atmosphere Autopilot.
Atmosphere Autopilot is free software: you can redistribute it and/or modify
it under the terms of the GNU General Public License as published by
the Free Software Foundation, either version 3 of the License, or
(at your option) any later version.
Atmosphere Autopilot is distributed in the hope that it will be useful,
but WITHOUT ANY WARRANTY; without even the implied warranty of
MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
GNU General Public License for more details.
You should have received a copy of the GNU General Public License
along with Atmosphere Autopilot.  If not, see <http://www.gnu.org/licenses/>. 
*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace AtmosphereAutopilot
{

	/// <summary>
	/// Controls angular velocity
	/// </summary>
	public abstract class AngularVelAdaptiveController : SISOController
	{
		protected int axis;

        protected InstantControlModel imodel;
		protected AngularAccAdaptiveController acc_controller;

		/// <summary>
		/// Create controller instance
		/// </summary>
		/// <param name="vessel">Vessel to control</param>
		/// <param name="module_name">Name of controller</param>
		/// <param name="wnd_id">unique for types window id</param>
		/// <param name="axis">Pitch = 0, roll = 1, yaw = 2</param>
		/// <param name="model">Flight model instance for adaptive control</param>
		protected AngularVelAdaptiveController(Vessel vessel, string module_name,
			int wnd_id, int axis)
			: base(vessel, module_name, wnd_id)
		{
			this.axis = axis;
            AutoTrim = false;
		}

		public override void InitializeDependencies(Dictionary<Type, AutopilotModule> modules)
		{
			this.imodel = modules[typeof(InstantControlModel)] as InstantControlModel;
		}

		protected override void OnActivate() 
        {
            imodel.Activate();
            acc_controller.Activate();
        }

        protected override void OnDeactivate()
        {
            imodel.Deactivate();
            acc_controller.Deactivate();
        }

		double time_in_regime = 0.0;

        [AutoGuiAttr("angular vel", false, "G8")]
        protected float vel;

        [AutoGuiAttr("output acceleration", false, "G8")]
        protected float output_acc;

        //[AutoGuiAttr("Kp", true, "G8")]
        float Kp = 8.0f;

        [GlobalSerializable("user_input_deriv_clamp")]
        [AutoGuiAttr("Smoothing", true, "G6")]
        public float user_input_deriv_clamp = 3.0f;

        [AutoGuiAttr("prev_input", false, "G6")]
        protected float prev_input;

		/// <summary>
		/// Main control function
		/// </summary>
		/// <param name="cntrl">Control state to change</param>
        public override float ApplyControl(FlightCtrlState cntrl, float target_value)
		{
            vel = imodel.AngularVel(axis);				    // get angular velocity

            float user_input = ControlUtils.get_neutralized_user_input(cntrl, axis);

            if (user_input != 0.0f || user_controlled)
            {
                // user is interfering with control
                float clamp = FlightInputHandler.fetch.precisionMode ?
                    0.33f * user_input_deriv_clamp * TimeWarp.fixedDeltaTime :
                    user_input_deriv_clamp * TimeWarp.fixedDeltaTime;
                if (FlightInputHandler.fetch.precisionMode)
                    user_input *= 0.33f;
                float delta_input = Common.Clampf(user_input - prev_input, clamp);
                user_input = prev_input + delta_input;
                prev_input = user_input;
                desired_v = user_input * max_v_construction;
                user_controlled = true;
            }
            else
            {
                // control from above
                desired_v = Common.Clampf(target_value, max_v_construction);
            }
            
            desired_v = process_desired_v(desired_v, user_controlled);      // moderation stage

            output_acc = get_desired_acc(desired_v);            // produce output

			// check if we're stable on given input value
            if (AutoTrim && vessel == AtmosphereAutopilot.Instance.ActiveVessel)
            {
                if (Math.Abs(vel) < 0.005f)
                {
                    time_in_regime += TimeWarp.fixedDeltaTime;
                }
                else
                {
                    time_in_regime = 0.0;
                }

                if (time_in_regime >= 5.0)
                    ControlUtils.set_trim(axis, imodel.ControlSurfPosHistory(axis).Average());
            }

            acc_controller.ApplyControl(cntrl, output_acc);

            return output_acc;
		}

        [VesselSerializable("max_v_construction")]
        [GlobalSerializable("max_v_construction")]
        [AutoGuiAttr("Max v construction", true, "G8")]
        public float max_v_construction = 0.5f;

        protected virtual float process_desired_v(float des_v, bool user_input) { return des_v; }

        protected virtual float get_desired_acc(float des_v) { return Kp * (desired_v - vel); }

        [AutoGuiAttr("DEBUG desired_v", false, "G8")]
        protected float desired_v;

        [GlobalSerializable("AutoTrim")]
        [AutoGuiAttr("AutoTrim", true)]
        public bool AutoTrim { get; set; }
	}


	//
	// Three realizations
	//

    public abstract class PitchYawAngularVelocityController : AngularVelAdaptiveController
    {
        protected PitchYawAngularVelocityController(Vessel vessel, string module_name,
            int wnd_id, int axis)
            : base(vessel, module_name, wnd_id, axis)
        { }

        protected Matrix eq_A = new Matrix(2, 2);
        protected Matrix eq_b = new Matrix(2, 1);
        protected Matrix eq_x;

        [AutoGuiAttr("max_input_aoa", false, "G6")]
        protected float max_input_aoa;

        [AutoGuiAttr("max_input_v", false, "G6")]
        protected float max_input_v;

        [AutoGuiAttr("min_input_aoa", false, "G6")]
        protected float min_input_aoa;

        [AutoGuiAttr("min_input_v", false, "G6")]
        protected float min_input_v;

        [AutoGuiAttr("max_g_aoa", false, "G6")]
        protected float max_g_aoa;

        [AutoGuiAttr("min_g_aoa", false, "G6")]
        protected float min_g_aoa;

        [AutoGuiAttr("max_g_v", false, "G6")]
        protected float max_g_v;

        [AutoGuiAttr("min_g_v", false, "G6")]
        protected float min_g_v;

        [AutoGuiAttr("max_aoa_v", false, "G6")]
        protected float max_aoa_v;

        [AutoGuiAttr("min_aoa_v", false, "G6")]
        protected float min_aoa_v;

        [AutoGuiAttr("moder_filter", true, "G6")]
        protected float moder_filter = 4.0f;

        protected Matrix state_mat = new Matrix(3, 1);
        protected Matrix input_mat = new Matrix(1, 1);

        protected LinearSystemModel lin_model;

        protected override float process_desired_v(float des_v, bool user_input)
        {
            float rad_max_aoa = max_aoa * dgr2rad;
            res_max_aoa = 100.0f;
            res_min_aoa = -100.0f;
            res_equilibr_v_upper = 0.0f;
            res_equilibr_v_lower = 0.0f;
            float cur_aoa = imodel.AoA(axis);
            float abs_cur_aoa = Math.Abs(cur_aoa);
            bool moderated = false;

            if (moderate_aoa && imodel.dyn_pressure > 100.0)
            {
                if (abs_cur_aoa < 0.35f)
                {
                    // We're in linear regime so we can update our limitations

                    // get equilibrium aoa and angular_v for 1.0 input
                    try
                    {
                        eq_A[0, 0] = lin_model.A[0, 0];
                        eq_A[0, 1] = lin_model.A[0, 1];
                        eq_A[1, 0] = lin_model.A[1, 0];
                        eq_A[1, 1] = 0.0;
                        eq_b[0, 0] = -(lin_model.A[0, 2] + lin_model.C[0, 0]);
                        eq_b[1, 0] = -(lin_model.A[1, 2] + lin_model.B[1, 0] + lin_model.C[1, 0]);
                        eq_A.old_lu = true;
                        eq_x = eq_A.SolveWith(eq_b);
                        if (!double.IsInfinity(eq_x[0, 0]) && !double.IsNaN(eq_x[0, 0]))
                        {
                            if (eq_x[0, 0] < 0.0)
                            {
                                // plane is statically unstable, in_eq_x solution is equilibrium on it's minimal stable aoa
                                min_input_aoa = (float)Common.simple_filter(0.6 * eq_x[0, 0], min_input_aoa, moder_filter);
                                min_input_v = (float)Common.simple_filter(0.6 * eq_x[1, 0], min_input_v, moder_filter);
                            }
                            else
                            {
                                // plane is statically stable, in_eq_x solution is equilibrium on it's maximal stable aoa
                                max_input_aoa = (float)Common.simple_filter(eq_x[0, 0], max_input_aoa, moder_filter);
                                max_input_v = (float)Common.simple_filter(eq_x[1, 0], max_input_v, moder_filter);
                            }

                            // get equilibrium aoa and angular_v for -1.0 input
                            eq_b[0, 0] = -lin_model.C[0, 0] + lin_model.A[0, 2];
                            eq_b[1, 0] = lin_model.A[1, 2] + lin_model.B[1, 0] - lin_model.C[1, 0];
                            eq_x = eq_A.SolveWith(eq_b);
                            if (!double.IsInfinity(eq_x[0, 0]) && !double.IsNaN(eq_x[0, 0]))
                            {
                                if (eq_x[0, 0] >= 0.0)
                                {
                                    // plane is statically unstable, in_eq_x solution is equilibrium on it's maximal stable aoa
                                    max_input_aoa = (float)Common.simple_filter(0.6 * eq_x[0, 0], max_input_aoa, moder_filter);
                                    max_input_v = (float)Common.simple_filter(0.6 * eq_x[1, 0], max_input_v, moder_filter);
                                }
                                else
                                {
                                    // plane is statically stable, in_eq_x solution is equilibrium on it's minimal stable aoa
                                    min_input_aoa = (float)Common.simple_filter(eq_x[0, 0], min_input_aoa, moder_filter);
                                    min_input_v = (float)Common.simple_filter(eq_x[1, 0], min_input_v, moder_filter);
                                }
                            }
                        }
                    }
                    catch (MSingularException) { }

                    // get equilibrium v for max_aoa
                    eq_A[0, 0] = lin_model.A[0, 1];
                    eq_A[0, 1] = lin_model.A[0, 2];
                    eq_A[1, 0] = lin_model.A[1, 1];
                    eq_A[1, 1] = lin_model.A[1, 2] + lin_model.B[1, 0];
                    eq_b[0, 0] = -(lin_model.A[0, 0] * rad_max_aoa + lin_model.C[0, 0]);
                    eq_b[1, 0] = -(lin_model.A[1, 0] * rad_max_aoa + lin_model.C[1, 0]);
                    eq_A.old_lu = true;
                    try
                    {
                        eq_x = eq_A.SolveWith(eq_b);
                        double new_max_aoa_v = eq_x[0, 0];
                        eq_b[0, 0] = -(lin_model.A[0, 0] * -rad_max_aoa + lin_model.C[0, 0]);
                        eq_b[1, 0] = -(lin_model.A[1, 0] * -rad_max_aoa + lin_model.C[1, 0]);
                        eq_x = eq_A.SolveWith(eq_b);
                        double new_min_aoa_v = eq_x[0, 0];
                        if (!double.IsInfinity(new_max_aoa_v) && !double.IsNaN(new_max_aoa_v)
                            && !double.IsInfinity(new_min_aoa_v) && !double.IsNaN(new_min_aoa_v))
                        {
                            max_aoa_v = (float)Common.simple_filter(new_max_aoa_v, max_aoa_v, moder_filter);
                            min_aoa_v = (float)Common.simple_filter(new_min_aoa_v, min_aoa_v, moder_filter);
                        }
                    }
                    catch (MSingularException) { }
                }

                // let's apply moderation with controllability region
                if (max_input_aoa < res_max_aoa)
                {
                    res_max_aoa = max_input_aoa;
                    res_equilibr_v_upper = max_input_v;
                }
                if (min_input_aoa > res_min_aoa)
                {
                    res_min_aoa = min_input_aoa;
                    res_equilibr_v_lower = min_input_v;
                }

                // apply simple AoA moderation
                if (rad_max_aoa < res_max_aoa)
                {
                    res_max_aoa = rad_max_aoa;
                    res_equilibr_v_upper = max_aoa_v;
                }
                if (-rad_max_aoa > res_min_aoa)
                {
                    res_min_aoa = -rad_max_aoa;
                    res_equilibr_v_lower = min_aoa_v;
                }

				moderated = true;
            }

            if (moderate_g && imodel.dyn_pressure > 100.0)
            {
                moderated = true;
                
                if (Math.Abs(lin_model.A[0, 0]) > 1e-5 && abs_cur_aoa < 0.35f)
                {
                    // model may be sane, let's update limitations
                    double gravity_acc = 0.0;
                    switch (axis)
                    {
                        case PITCH:
                            gravity_acc = imodel.pitch_gravity_acc + imodel.pitch_noninert_acc;
                            break;
                        case YAW:
                            gravity_acc = imodel.yaw_gravity_acc + imodel.yaw_noninert_acc;
                            break;
                        default:
                            gravity_acc = 0.0;
                            break;
                    }
                    // get equilibrium aoa and angular v for max_g g-force
                    max_g_v = (float)Common.simple_filter(
                        (max_g_force * 9.81 + gravity_acc) / vessel.srfSpeed,
                        max_g_v, moder_filter);
                    min_g_v = (float)Common.simple_filter(
                        (-max_g_force * 9.81 + gravity_acc) / vessel.srfSpeed,
                        min_g_v, moder_filter);
                    // get equilibrium v for max_aoa
                    eq_A[0, 0] = lin_model.A[0, 0];
                    eq_A[0, 1] = lin_model.A[0, 2];
                    eq_A[1, 0] = lin_model.A[1, 0];
                    eq_A[1, 1] = lin_model.A[1, 2] + lin_model.B[1, 0];
                    eq_b[0, 0] = -(max_g_v + lin_model.C[0, 0]);
                    eq_b[1, 0] = -lin_model.C[1, 0];
                    eq_A.old_lu = true;
                    try
                    {
                        eq_x = eq_A.SolveWith(eq_b);
                        double new_max_g_aoa = eq_x[0, 0];
                        eq_b[0, 0] = -(min_g_v + lin_model.C[0, 0]);
                        eq_x = eq_A.SolveWith(eq_b);
                        double new_min_g_aoa = eq_x[0, 0];
                        if (!double.IsInfinity(new_max_g_aoa) && !double.IsNaN(new_max_g_aoa) &&
                            !double.IsInfinity(new_min_g_aoa) && !double.IsNaN(new_min_g_aoa))
                        {
                            max_g_aoa = (float)Common.simple_filter(new_max_g_aoa, max_g_aoa, moder_filter);
                            min_g_aoa = (float)Common.simple_filter(new_min_g_aoa, min_g_aoa, moder_filter);
                        }
                    }
                    catch (MSingularException) { }
                }
                
                // apply moderation
                if (max_g_aoa < 2.0 && max_g_aoa > 0.0 && min_g_aoa > -2.0 && max_g_aoa > min_g_aoa)       // sanity check
                {
                    if (max_g_aoa < res_max_aoa)
                    {
                        res_max_aoa = max_g_aoa;
                        res_equilibr_v_upper = max_g_v;
                    }
                    if (min_g_aoa > res_min_aoa)
                    {
                        res_min_aoa = min_g_aoa;
                        res_equilibr_v_lower = min_g_v;
                    }
                }
            }

            // let's get non-overshooting max v value, let's call it transit_max_v
            // we start on 0.0 aoa with transit_max_v and we must not overshoot res_max_aoa
            // while applying -1.0 input all the time
			if (abs_cur_aoa < 0.26f && imodel.dyn_pressure > 10.0)
			{
				double transit_max_aoa = Math.Min(rad_max_aoa, res_max_aoa);
				state_mat[0, 0] = transit_max_aoa / 2.0;
				state_mat[2, 0] = -1.0;
				input_mat[0, 0] = -1.0;
				double acc = lin_model.eval_row(1, state_mat, input_mat);
				float new_dyn_max_v =
					(float)Math.Sqrt(transit_max_aoa * (-acc));
				if (float.IsNaN(new_dyn_max_v))
				{
					if (old_dyn_max_v != 0.0f)
						transit_max_v = old_dyn_max_v;
					else
						old_dyn_max_v = max_v_construction;
				}
				else
				{
					new_dyn_max_v = Common.Clampf(new_dyn_max_v, max_v_construction);
					transit_max_v = (float)Common.simple_filter(new_dyn_max_v, transit_max_v, moder_filter);
					old_dyn_max_v = transit_max_v;
				}
			}
			else
				if (imodel.dyn_pressure <= 10.0)
					transit_max_v = max_v_construction;
				else
				{
					if (old_dyn_max_v != 0.0f)
						transit_max_v = old_dyn_max_v;
					else
					{
						old_dyn_max_v = max_v_construction;
						transit_max_v = max_v_construction;
					}
				}
            
            // if the user is in charge, let's hold surface-relative pitch angle
            float v_offset = 0.0f;
            if (user_input && vessel.obt_velocity.sqrMagnitude > 1.0)
            {
                if (FlightUIController.speedDisplayMode == FlightUIController.SpeedDisplayModes.Surface)
                {
                    Vector3 planet2vessel = vessel.GetWorldPos3D() - vessel.mainBody.position;
                    Vector3 still_ang_v = Vector3.Cross(vessel.obt_velocity, planet2vessel) / planet2vessel.sqrMagnitude;
                    Vector3 principal_still_ang_v = imodel.world_to_cntrl_part * still_ang_v;
                    v_offset = principal_still_ang_v[axis];
                }
            }

            // desired_v moderation section
            float scaled_restrained_v;
            float normalized_des_v = user_input ? des_v / max_v_construction : des_v / transit_max_v;
            if (float.IsInfinity(normalized_des_v) || float.IsNaN(normalized_des_v))
                normalized_des_v = 0.0f;
            normalized_des_v = Common.Clampf(normalized_des_v, 1.0f);
            if (moderated)
            {
                if (des_v >= 0.0f)
                {
                    scaled_aoa = Common.Clampf((res_max_aoa - cur_aoa) / (res_max_aoa - res_min_aoa), 1.0f);
                    if (scaled_aoa < 0.0f)
                    {
                        scaled_aoa *= 2.0f;
                    }
                    scaled_restrained_v = Math.Min(transit_max_v * normalized_des_v * scaled_aoa +
						res_equilibr_v_upper * (1.0f - Math.Abs(scaled_aoa)) + v_offset,
                        transit_max_v * normalized_des_v + v_offset);
                }
                else
                {
                    scaled_aoa = Common.Clampf((res_min_aoa - cur_aoa) / (res_min_aoa - res_max_aoa), 1.0f);
                    if (scaled_aoa < 0.0f)
                    {
                        scaled_aoa *= 2.0f;
                    }
                    scaled_restrained_v = Math.Max(transit_max_v * normalized_des_v * scaled_aoa +
						res_equilibr_v_lower * (1.0f - Math.Abs(scaled_aoa)) + v_offset,
                        transit_max_v * normalized_des_v + v_offset);
                }
            }
            else
                scaled_restrained_v = transit_max_v * normalized_des_v + v_offset;
            
            des_v = scaled_restrained_v;
            return des_v;
        }

        [AutoGuiAttr("quadr Kp", true, "G6")]
        protected float quadr_Kp = 0.3f;

        [AutoGuiAttr("kacc_quadr", false, "G6")]
        protected float kacc_quadr;
        protected bool first_quadr = true;

        [AutoGuiAttr("kacc_smoothing", true, "G5")]
        protected float kacc_smoothing = 10.0f;

        [AutoGuiAttr("relaxation_k", true, "G5")]
        protected float relaxation_k = 1.0f;

        [AutoGuiAttr("relaxation_Kp", true, "G5")]
        protected float relaxation_Kp = 0.5f;

        [AutoGuiAttr("relaxation_frame", true)]
        protected int relaxation_frame = 1;

        [AutoGuiAttr("relaxation_frame", false)]
        protected int relax_count = 0;

        protected override float get_desired_acc(float des_v)
        {
            float new_kacc_quadr = 0.0f;
            if (AtmosphereAutopilot.AeroModel == AtmosphereAutopilot.AerodinamycsModel.FAR)
                new_kacc_quadr = (float)(quadr_Kp * (lin_model.A[1, 2] * lin_model.B[2, 0] + lin_model.B[1, 0]));
            if (AtmosphereAutopilot.AeroModel == AtmosphereAutopilot.AerodinamycsModel.Stock)
                new_kacc_quadr = (float)(quadr_Kp * (lin_model.A[1, 2] * lin_model.C[2, 0] + lin_model.B[1, 0]));
            if (float.IsNaN(new_kacc_quadr))
                return base.get_desired_acc(des_v);
            if (first_quadr)
                kacc_quadr = new_kacc_quadr;
            else
                kacc_quadr = (float)Common.simple_filter(new_kacc_quadr, kacc_quadr, kacc_smoothing);
            if (kacc_quadr < 1e-3)
                return base.get_desired_acc(des_v);
            first_quadr = false;
            float v_error = vel - des_v;
            double quadr_x;
            float desired_deriv;
            float dt = TimeWarp.fixedDeltaTime;
            if (v_error >= 0.0)
            {
                quadr_x = -Math.Sqrt(v_error / kacc_quadr);
                if (quadr_x >= -relaxation_k * dt)
                {
                    if (++relax_count > relaxation_frame)
                    {
                        float avg_vel = 0.0f;
                        for (int i = 0; i < relaxation_frame; i++)
                            avg_vel += imodel.AngularVelHistory(axis).getFromTail(i);
                        avg_vel /= (float)relaxation_frame;
                        v_error = avg_vel - des_v;
                        if (relax_count > relaxation_frame * 2)
                            relax_count--;
                    }
                    desired_deriv = (float)(relaxation_Kp * -v_error / (Math.Ceiling(relaxation_k) * dt));
                }
                else
                {
                    relax_count = 0;
                    double leftover_dt = Math.Min(dt, -quadr_x);
                    desired_deriv = (float)(kacc_quadr * Math.Pow(quadr_x + leftover_dt, 2.0) - kacc_quadr * quadr_x * quadr_x) / dt;
                }
            }
            else
            {
                quadr_x = -Math.Sqrt(v_error / -kacc_quadr);
                if (quadr_x >= -relaxation_k * dt)
                {
                    if (++relax_count > relaxation_frame)
                    {
                        float avg_vel = 0.0f;
                        for (int i = 0; i < relaxation_frame; i++)
                            avg_vel += imodel.AngularVelHistory(axis).getFromTail(i);
                        avg_vel /= (float)relaxation_frame;
                        v_error = avg_vel - des_v;
                        if (relax_count > relaxation_frame * 2)
                            relax_count--;
                    }
                    desired_deriv = (float)(relaxation_Kp * -v_error / (Math.Ceiling(relaxation_k) * dt));
                }
                else
                {
                    relax_count = 0;
                    double leftover_dt = Math.Min(dt, -quadr_x);
                    desired_deriv = (float)(-kacc_quadr * Math.Pow(quadr_x + leftover_dt, 2.0) + kacc_quadr * quadr_x * quadr_x) / dt;
                }
            }
            return desired_deriv;
        }

        [AutoGuiAttr("transit_max_v", false, "G6")]
        public float transit_max_v;

        protected float old_dyn_max_v;

        [AutoGuiAttr("res_max_aoa", false, "G6")]
        public float res_max_aoa;

        [AutoGuiAttr("res_equolibr_v_upper", false, "G6")]
        public float res_equilibr_v_upper;

        [AutoGuiAttr("res_min_aoa", false, "G6")]
        public float res_min_aoa;

        [AutoGuiAttr("res_equolibr_v_lower", false, "G6")]
        public float res_equilibr_v_lower;

        [AutoGuiAttr("DEBUG scaled_aoa", false, "G6")]
        protected float scaled_aoa;

        [AutoGuiAttr("Moderate AoA", true, null)]
        public bool moderate_aoa = true;

        [AutoGuiAttr("Moderate G-force", true, null)]
        public bool moderate_g = true;

        [VesselSerializable("max_aoa")]
        [AutoGuiAttr("max AoA", true, "G6")]
        public float max_aoa = 15.0f;

        [VesselSerializable("max_g_force")]
        [AutoGuiAttr("max G-force", true, "G6")]
        protected float max_g_force = 5.0f;
    }

    public sealed class PitchAngularVelocityController : PitchYawAngularVelocityController
    {
        internal PitchAngularVelocityController(Vessel vessel)
            : base(vessel, "Pitch ang vel controller", 1234444, PITCH)
        { }

		public override void InitializeDependencies(Dictionary<Type, AutopilotModule> modules)
		{
			base.InitializeDependencies(modules);
			this.acc_controller = modules[typeof(PitchAngularAccController)] as PitchAngularAccController;
            this.lin_model = imodel.pitch_rot_model_gen;
		}
    }

	public sealed class RollAngularVelocityController : AngularVelAdaptiveController
	{
		internal RollAngularVelocityController(Vessel vessel)
            : base(vessel, "Roll ang vel controller", 1234445, ROLL)
		{ }

		public override void InitializeDependencies(Dictionary<Type, AutopilotModule> modules)
		{
			base.InitializeDependencies(modules);
			this.acc_controller = modules[typeof(RollAngularAccController)] as RollAngularAccController;
		}

        [AutoGuiAttr("max_input_v", false, "G6")]
        float max_input_v;

        [AutoGuiAttr("min_input_v", false, "G6")]
        float min_input_v;

        [AutoGuiAttr("moder_filter", true, "G6")]
        float moder_filter = 4.0f;

        Matrix state_mat = new Matrix(2, 1);
        Matrix input_mat = new Matrix(3, 1);

        protected override float process_desired_v(float des_v, bool user_input)
        {
            float cur_aoa = imodel.AoA(YAW);
            
            // let's find maximum angular v on 0.0 AoA and 0.0 Yaw input from model
            if (cur_aoa < 0.3 && imodel.dyn_pressure > 10.0)
            {
                float new_max_input_v =
                    (float)((imodel.roll_rot_model_gen.C[0, 0] + imodel.roll_rot_model_gen.B[0, 0] + imodel.roll_rot_model_gen.A[0, 1]) /
                        -imodel.roll_rot_model_gen.A[0, 0]);
                float new_min_input_v =
                    (float)((imodel.roll_rot_model_gen.C[0, 0] - imodel.roll_rot_model_gen.B[0, 0] - imodel.roll_rot_model_gen.A[0, 1]) /
                        -imodel.roll_rot_model_gen.A[0, 0]);
                if (!float.IsInfinity(new_max_input_v) && !float.IsNaN(new_max_input_v) &&
                    !float.IsInfinity(new_min_input_v) && !float.IsNaN(new_min_input_v))
                {
                    max_input_v = (float)Common.simple_filter(new_max_input_v, max_input_v, moder_filter);
                    min_input_v = (float)Common.simple_filter(new_min_input_v, min_input_v, moder_filter);
                }
            }

            // desired_v moderation section
            if (des_v >= 0.0f)
            {
                float normalized_des_v = user_input ? des_v / max_v_construction : des_v / max_input_v;
                if (float.IsInfinity(normalized_des_v) || float.IsNaN(normalized_des_v))
                    normalized_des_v = 0.0f;
                normalized_des_v = Common.Clampf(normalized_des_v, 1.0f);
                float scaled_restrained_v = Math.Min(max_input_v, max_v_construction);
                des_v = normalized_des_v * scaled_restrained_v;
            }
            else
            {
                float normalized_des_v = user_input ? des_v / -max_v_construction : des_v / min_input_v;
                if (float.IsInfinity(normalized_des_v) || float.IsNaN(normalized_des_v))
                    normalized_des_v = 0.0f;
                normalized_des_v = Common.Clampf(normalized_des_v, 1.0f);
                float scaled_restrained_v = Math.Max(min_input_v, -max_v_construction);
                des_v = normalized_des_v * scaled_restrained_v;
            }

            return des_v;
        }

        [AutoGuiAttr("quadr Kp", true, "G6")]
        float quadr_Kp = 0.4f;

        [AutoGuiAttr("kacc_quadr", false, "G6")]
        float kacc_quadr;
        bool first_quadr = true;

        [AutoGuiAttr("kacc_smoothing", true, "G5")]
        float kacc_smoothing = 10.0f;

        [AutoGuiAttr("relaxation_k", true, "G5")]
        float relaxation_k = 2.0f;

        [AutoGuiAttr("relaxation_Kp", true, "G5")]
        float relaxation_Kp = 0.5f;

        [AutoGuiAttr("relaxation_frame", true)]
        int relaxation_frame = 1;

        [AutoGuiAttr("relaxation_frame", false)]
        int relax_count = 0;

        protected override float get_desired_acc(float des_v)
        {
            float new_kacc_quadr = 0.0f;
            if (AtmosphereAutopilot.AeroModel == AtmosphereAutopilot.AerodinamycsModel.FAR)
                new_kacc_quadr = (float)(quadr_Kp * (imodel.roll_rot_model_gen.A[0, 1] * imodel.roll_rot_model_gen.B[1, 0] + imodel.roll_rot_model_gen.B[0, 0]));
            if (AtmosphereAutopilot.AeroModel == AtmosphereAutopilot.AerodinamycsModel.Stock)
                new_kacc_quadr = (float)(quadr_Kp * (imodel.roll_rot_model_gen.A[0, 1] * imodel.roll_rot_model_gen.C[1, 0] + imodel.roll_rot_model_gen.B[0, 0]));
            if (float.IsNaN(new_kacc_quadr))
                return base.get_desired_acc(des_v);
            if (first_quadr)
                kacc_quadr = new_kacc_quadr;
            else
                kacc_quadr = (float)Common.simple_filter(new_kacc_quadr, kacc_quadr, kacc_smoothing);
            if (kacc_quadr < 1e-3)
                return base.get_desired_acc(des_v);
            first_quadr = false;
            float v_error = vel - des_v;
            double quadr_x;
            float desired_deriv;
            float dt = TimeWarp.fixedDeltaTime;
            if (v_error >= 0.0)
            {
                quadr_x = -Math.Sqrt(v_error / kacc_quadr);
                if (quadr_x >= -relaxation_k * dt)
                {
                    if (++relax_count > relaxation_frame)
                    {
                        float avg_vel = 0.0f;
                        for (int i = 0; i < relaxation_frame; i++)
                            avg_vel += imodel.AngularVelHistory(ROLL).getFromTail(i);
                        avg_vel /= (float)relaxation_frame;
                        v_error = avg_vel - des_v;
                        if (relax_count > relaxation_frame * 2)
                            relax_count--;
                    }
                    desired_deriv = (float)(relaxation_Kp * -v_error / (Math.Ceiling(relaxation_k) * dt));
                }
                else
                {
                    relax_count = 0;
                    double leftover_dt = Math.Min(dt, -quadr_x);
                    desired_deriv = (float)(kacc_quadr * Math.Pow(quadr_x + leftover_dt, 2.0) - kacc_quadr * quadr_x * quadr_x) / dt;
                }
            }
            else
            {
                quadr_x = -Math.Sqrt(v_error / -kacc_quadr);
                if (quadr_x >= -relaxation_k * dt)
                {
                    if (++relax_count > relaxation_frame)
                    {
                        float avg_vel = 0.0f;
                        for (int i = 0; i < relaxation_frame; i++)
                            avg_vel += imodel.AngularVelHistory(ROLL).getFromTail(i);
                        avg_vel /= (float)relaxation_frame;
                        v_error = avg_vel - des_v;
                        if (relax_count > relaxation_frame * 2)
                            relax_count--;
                    }
                    desired_deriv = (float)(relaxation_Kp * -v_error / (Math.Ceiling(relaxation_k) * dt));
                }
                else
                {
                    double leftover_dt = Math.Min(dt, -quadr_x);
                    desired_deriv = (float)(-kacc_quadr * Math.Pow(quadr_x + leftover_dt, 2.0) + kacc_quadr * quadr_x * quadr_x) / dt;
                    relax_count = 0;
                }
            }
            return desired_deriv;
        }
	}

    public sealed class YawAngularVelocityController : PitchYawAngularVelocityController
	{
		internal YawAngularVelocityController(Vessel vessel)
            : base(vessel, "Yaw ang vel controller", 1234446, YAW)
		{ }

		public override void InitializeDependencies(Dictionary<Type, AutopilotModule> modules)
		{
			base.InitializeDependencies(modules);
			this.acc_controller = modules[typeof(YawAngularAccController)] as YawAngularAccController;
            this.lin_model = imodel.yaw_rot_model_gen;
		}
	}

}
