﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using UnityEngine;
using System.IO;

namespace AtmosphereAutopilot
{
	/// <summary>
	/// Class for short-motion model approximation
	/// </summary>
	class InstantControlModel
	{
		public static readonly int PITCH = 0;
		public static readonly int ROLL = 1;
		public static readonly int YAW = 2;

		Vessel vessel;

		public InstantControlModel(Vessel v)
		{
			vessel = v;
			for (int i = 0; i < 3; i++)
			{
				input_buf[i] = new CircularBuffer<double>(BUFFER_SIZE, true);
				angular_v[i] = new CircularBuffer<double>(BUFFER_SIZE, true);
				angular_dv[i] = new CircularBuffer<double>(BUFFER_SIZE, true);
				angular_d2v[i] = new CircularBuffer<double>(BUFFER_SIZE, true);
			}
            vessel.OnFlyByWire += new FlightInputCallback(OnFlyByWire);
		}

		static readonly int BUFFER_SIZE = 50;

		public CircularBuffer<double>[] input_buf = new CircularBuffer<double>[3];	// control input value
		public CircularBuffer<double>[] angular_v = new CircularBuffer<double>[3];	// angular v
		public CircularBuffer<double>[] angular_dv = new CircularBuffer<double>[3];	// dv/dt
		public CircularBuffer<double>[] angular_d2v = new CircularBuffer<double>[3];// d2v/dt2

		double prev_dt = 1.0;		// dt in previous call
		int stable_dt = 0;			// counts amount of stable dt intervals

        void OnFlyByWire(FlightCtrlState state)
		{
			if (vessel.checkLanded())           // ground breaks the model
			{
				stable_dt = 0;
				return;
			}
			
			double dt = TimeWarp.fixedDeltaTime;
			check_dt(dt);
			update_buffers(state);
			update_model();

			prev_dt = dt;
		}

		void check_dt(double new_dt)
		{
			if (Math.Abs(new_dt / prev_dt - 1.0) < 0.1)
				stable_dt = Math.Min(1000, stable_dt + 1);
			else
				stable_dt = 0;
		}

		void update_buffers(FlightCtrlState state)
		{
			for (int i = 0; i < 3; i++)
			{
				input_buf[i].Put(getControlFromState(state, i));
				angular_v[i].Put(vessel.angularVelocity[i]);
				if (stable_dt >= 1)
					angular_dv[i].Put(
                        derivative1_short(
							angular_v[i].getFromTail(1),
							angular_v[i].getFromTail(0),
							prev_dt));
				if (stable_dt >= 2)
					angular_d2v[i].Put(
						derivative2(
							angular_v[i].getFromTail(2),
							angular_v[i].getFromTail(1),
							angular_v[i].getFromTail(0),
							prev_dt));
			}
		}

		double getControlFromState(FlightCtrlState state, int control)
		{
			if (control == PITCH)
				return state.pitch;
			if (control == ROLL)
				return state.roll;
			if (control == YAW)
				return state.yaw;
			return 0.0;
		}

        public static double derivative1_short(double y0, double y1, double dt)    // first derivative
        {
            return (y1 - y0) / dt;
        }

        public static double derivative1_middle(double y0, double y2, double dt)    // first derivative
        {
            return (y2 - y0) / dt * 0.5;
        }

		public static double derivative1(double y0, double y1, double y2, double dt)    // first derivative
		{
			return (y0 - 4 * y1 + 3 * y2) / dt * 0.5;
		}

		public static double derivative2(double y0, double y1, double y2, double dt)    // second derivative
		{
			return (y0 - 2 * y1 + y2) / dt / dt;
		}

		//
		// Short term Model section
		//

		// Basic approximation:
        // angular_dv = k_control_sqr * control^2 + k_control * control + c_control
		public double[] k_control = new double[3];
		public double[] c_control = new double[3];

        enum TransitionType
        {
            stationary,
            linear
        };

        TransitionType maneuver = TransitionType.stationary;

        class ModelException : Exception { }

		public void update_model()
		{
			if (stable_dt < 2)
				return;

			for (int i = 0; i < 3; i++)
			{
                //if (Math.Abs(input_buf[i].getLast() - input_buf[i].getFromTail(1)) < 1e-4)
                //{
                //    // stationary turn
                //    c_control[i] = angular_dv[i].getLast() - input_buf[i].getLast() * k_control[i];
                //    maneuver = TransitionType.stationary;
                //    return;
                //}
                //Matrix ang_dv_m, coeff, result;
                //// need linear
                //ang_dv_m = new Matrix(2, 1);				        // column of angular v derivatives
                //ang_dv_m[0, 0] = angular_dv[i].getFromTail(1) +
                //    angular_d2v[i].getFromTail(0) * prev_dt;        // account for regime switch. dv2 lags for 1 cycle
                //ang_dv_m[1, 0] = angular_dv[i].getFromTail(0);
                //coeff = new Matrix(2, 2);
                //coeff[0, 0] = input_buf[i].getFromTail(1);
                //coeff[1, 0] = input_buf[i].getFromTail(0);
                //coeff[0, 1] = coeff[1, 1] = 1.0;
                //try
                //{
                //    result = coeff.SolveWith(ang_dv_m);
                //    if (!double.IsInfinity(result[0, 0]) && !double.IsNaN(result[0, 0]))
                //        k_control[i] = result[0, 0];
                //    if (!double.IsInfinity(result[1, 0]) && !double.IsNaN(result[1, 0]))
                //        c_control[i] = result[1, 0];
                //    maneuver = TransitionType.linear;
                //}
                //catch { maneuver = TransitionType.stationary; }

                //
                // 3D plane method
                //
                // Find a plane in angular_dv - time - control space
                //Vector3d point0 = new Vector3d(0.0, input_buf[i].getFromTail(3), angular_dv[i].getFromTail(2));
                //Vector3d point1 = new Vector3d(prev_dt, input_buf[i].getFromTail(2), angular_dv[i].getFromTail(1));
                //Vector3d point2 = new Vector3d(2.0 * prev_dt, input_buf[i].getFromTail(1), angular_dv[i].getFromTail(0));
                //Vector3d normal = Vector3d.Cross((point1 - point0).normalized, (point2 - point0).normalized);
                //if (normal.magnitude < 1e-3)
                //{
                //    // vectors are too close and plane is badly-defined, assume transition stationary
                //    c_control[i] = angular_dv[i].getLast() - input_buf[i].getLast() * k_control[i];
                //    maneuver = TransitionType.stationary;
                //}
                //else
                //{
                //    // plane is defined, need to get projection to angular_dv - control plane
                //    normal.x = 0.0;     // zero out time coordinate
                //    if (normal.z < 0)
                //        normal = -normal;
                //    double tangent = -normal.y / normal.z;
                //    if (!double.IsInfinity(tangent) && !double.IsNaN(tangent) && (tangent > -1e4) && (tangent < 1e4))
                //    {
                //        k_control[i] = Common.Clamp(tangent, 1e3);
                //        c_control[i] = angular_dv[i].getLast() - input_buf[i].getLast() * tangent;
                //        maneuver = TransitionType.linear;
                //    }
                //    else
                //    {
                //        c_control[i] = angular_dv[i].getLast() - input_buf[i].getLast() * k_control[i];
                //        maneuver = TransitionType.stationary;
                //    }
                //}

                double dv_control0 = 0, dv_control1 = 0, d2v_control = 0;
                int i0 = 0, i1 = 0;
                try
                {
                    // we need to find variable input
                    for (i1 = 1; i1 < angular_dv[i].Size; i1++)
                    {
                        if (input_buf[i].getFromTail(i1) != input_buf[i].getFromTail(0))
                            break;
                    }
                    for (i0 = i1 + 1; i0 < angular_dv[i].Size; i0++)
                    {
                        if (input_buf[i].getFromTail(i1) != input_buf[i].getFromTail(i0))
                            break;
                    }
                    if (i0 >= input_buf[i].Size)
                        throw new ModelException();
                    dv_control0 = (angular_dv[i].getFromTail(i1) - angular_dv[i].getFromTail(i0)) /
                        (input_buf[i].getFromTail(i1) - input_buf[i].getFromTail(i0));
                    if (double.IsNaN(dv_control0) || double.IsInfinity(dv_control0))
                        throw new ModelException();
                    dv_control1 = (angular_dv[i].getFromTail(0) - angular_dv[i].getFromTail(i1)) /
                        (input_buf[i].getFromTail(0) - input_buf[i].getFromTail(i1));
                    if (double.IsNaN(dv_control1) || double.IsInfinity(dv_control1))
                        throw new ModelException();
                    d2v_control = (dv_control1 - dv_control0) /
                        (input_buf[i].getFromTail(0) - input_buf[i].getFromTail(i1));
                    if (double.IsNaN(d2v_control) || double.IsInfinity(d2v_control))
                        throw new ModelException();
                    k_control[i] = d2v_control;
                }
                catch (ModelException) 
                {
                    Debug.Log("[AUTOPILOT]: " + dv_control0.ToString("G8") + ' ' + dv_control1.ToString("G8") +
                        ' ' + d2v_control.ToString("G8") + " input = " + input_buf[i].getFromTail(2).ToString("G8") + ' ' +
                        input_buf[i].getFromTail(1).ToString("G8") + ' ' + input_buf[i].getFromTail(0).ToString("G8"));
                }
                finally
                {
                    c_control[i] = angular_dv[i].getLast() - input_buf[i].getLast() * k_control[i];
                }
			}
		}

        public double get_short_input_for_axis(int axis, double desired_angular_dv, double current_input)
        {
            //if (maneuver == TransitionType.linear || maneuver == TransitionType.stationary)
            //{
            //    double k = k_control[axis];
            //    if (double.IsNaN(k) || double.IsInfinity(k) || Math.Abs(k) < 1e-6)
            //        return double.NaN;
            //    double h = desired_angular_dv - c_control[axis];
            //    double x = h / k;
            //    return x;
            //}
            //return double.NaN;
            double dv = angular_dv[axis].getLast();
            double ddv = angular_d2v[axis].getLast() * prev_dt;
            double dinput = (desired_angular_dv - dv - ddv) / k_control[axis];
            if (double.IsInfinity(dinput))
                return current_input;
            return input_buf[axis].getLast() + dinput;
        }

        public static double closest(double target, double x1, double x2)
        {
            if (Math.Abs(x1 - target) >= Math.Abs(x2 - target))
                return x2;
            return x1;
        }

		//
		// GUI section
		//

		bool gui_shown = false;
		public void toggleGUI()
		{
			gui_shown = !gui_shown;
		}

		protected Rect window = new Rect(250.0f, 50.0f, 350.0f, 200.0f);

		public void drawGUI()
		{
			if (!gui_shown)
				return;
			window = GUILayout.Window(65448, window, _drawGUI, "Instant control model");
		}

		static readonly string[] axis_names = { "pitch", "roll", "yaw" };

		void _drawGUI(int id)
		{
			GUILayout.BeginVertical();
			for (int i = 0; i < 3; i++)
			{
				GUILayout.Label(axis_names[i] + " ang vel = " + angular_v[i].getLast().ToString("G8"));
				GUILayout.Label(axis_names[i] + " ang vel d1 = " + angular_dv[i].getLast().ToString("G8"));
				GUILayout.Label(axis_names[i] + " ang vel d2 = " + angular_d2v[i].getLast().ToString("G8"));
				GUILayout.Label(axis_names[i] + " K1 = " + k_control[i].ToString("G8"));
				GUILayout.Label(axis_names[i] + " C = " + c_control[i].ToString("G8"));
				GUILayout.Space(10);
			}
			GUILayout.EndVertical();
			GUI.DragWindow();
		}
	}

	/// <summary>
	/// Class for current vessel flight model calculations
	/// </summary>
	class FlightModel
	{
		/// <summary>
		/// Enumeration of all handeled flight parameters
		/// </summary>
		enum FCharacter
		{
			pitch = 0,		// pitch control value
			roll,			// roll control value
			yaw,			// yaw control value
			aoa_pitch,
			aoa_slide,
            aoa_mul_pitch,  // aoa_pitch * pitch
			com_horiz,		// angle between vessel forward and horizont
			surf_roll,		// angle between vessel wings and horizont
			dyn_pressure
		}

        public const int Param_count = 9;

        /// <summary>
        /// Enumeration of parameters model is trying to predict
        /// </summary>
        enum FControl
        {
            avd_pitch = 0,  // pitch angular velocity derivative
            avd_roll,       // roll angular velocity derivative
            avd_yaw
        }

		public FlightModel(Vessel vessel)
		{
            this.vessel = vessel;
			for (int i = 0; i < Param_count; i++)
				telemetry[i] = new CircularBuffer<double>(Steps_remembered, true);
            for (int i = 0; i < 3; i++)
            {
                angular_velocities[i] = new CircularBuffer<double>(Steps_remembered, true);
                angular_derivatives[i] = new CircularBuffer<double>(Steps_remembered, true);
                model_linear_k[i] = new double[Param_count];
                model_parameters_importance[i] = new double[Param_count];
            }
            set_inportance();
            vessel.OnFlyByWire += new FlightInputCallback(OnFlyByWire);

            // GUI
            //labelstyle.fontSize = 7;
            //labelstyle.margin = new RectOffset(2, 2, 2, 2);
		}

        Vessel vessel;
		
		public const int Steps_remembered = 15;        // time slices to remember. Affects performance
        int dt_stable = 0;
        double last_dt = 1.0;           // last delta_t

		public void OnFlyByWire(FlightCtrlState state)
		{
            if (vessel.checkLanded())           // ground breaks the model
            {
                dt_stable = 0;
                return;
            }

            // record flight readings
			telemetry[(int)FCharacter.pitch].Put(state.pitch);
			telemetry[(int)FCharacter.roll].Put(state.roll);
			telemetry[(int)FCharacter.yaw].Put(state.yaw);

            Vector3 tmpVec = vessel.ReferenceTransform.up * Vector3.Dot(vessel.ReferenceTransform.up, vessel.srf_velocity.normalized) + vessel.ReferenceTransform.forward * Vector3.Dot(vessel.ReferenceTransform.forward, vessel.srf_velocity.normalized);
            double aoa = Math.Asin(Vector3.Dot(vessel.ReferenceTransform.forward.normalized, tmpVec.normalized));
            telemetry[(int)FCharacter.aoa_pitch].Put(aoa);

            telemetry[(int)FCharacter.aoa_mul_pitch].Put(state.pitch * aoa);

            telemetry[(int)FCharacter.aoa_slide].Put(0);

            double pitch = Math.Abs(Vector3.Cross(vessel.transform.up.normalized, vessel.upAxis).magnitude);
            telemetry[(int)FCharacter.com_horiz].Put(pitch);

            telemetry[(int)FCharacter.surf_roll].Put(0);

            telemetry[(int)FCharacter.dyn_pressure].Put(vessel.srf_velocity.sqrMagnitude * FlightGlobals.getStaticPressure());

            angular_velocities[(int)FControl.avd_pitch].Put(vessel.angularVelocity.x);
            angular_velocities[(int)FControl.avd_roll].Put(vessel.angularVelocity.y);
            angular_velocities[(int)FControl.avd_yaw].Put(vessel.angularVelocity.z);

            // update
            update_derivatives();
            predict();
            solve();
		}

        // Flight characteristics buffers
		public CircularBuffer<double>[] telemetry = new CircularBuffer<double>[Param_count];
        public CircularBuffer<double>[] angular_velocities = new CircularBuffer<double>[3];
        public CircularBuffer<double>[] angular_derivatives = new CircularBuffer<double>[3];

        // Model characteristics buffers
        public double[][] model_linear_k = new double[3][];                        // linear gain coeffitients
        double[][] model_parameters_importance = new double[3][];           // importance of flight parameters
        double[] error = new double[3];                                     // errors of angular vel derivatives prediction
        int solve_count = 0;                                                // matrix solve counter. Used for stabilizing model
        int solve_memory = 10;                                               // how many last solutions will be accounted when building a model

        void update_derivatives()
        {
            double dt = TimeWarp.fixedDeltaTime;
            if (Math.Abs(dt / last_dt - 1.0) < 0.1)
            {
                // dt is roughly constant
                dt_stable = Math.Min(dt_stable + 1, 100);       // increase dt stability counter
                if (dt_stable >= 2)                             // if dt is stable long enough
                    for (int i = 0; i < 3; i++)
                        if (angular_velocities[i].Size >= 3)
                            angular_derivatives[i].Put(derivative1(angular_velocities[i].getFromTail(2),
                                angular_velocities[i].getFromTail(1), angular_velocities[i].getFromTail(0), dt));
            }
            else
            {
                // new delta_t
                dt_stable = 0;
                solve_count = 0;
            }
            last_dt = dt;
        }

        void predict()
        {
            if (dt_stable < 2 || telemetry[0].Size < 1)
                return;
            double[] prediction = new double[3];
            for (int i = 0; i < 3; i++)
            {
                prediction[i] = 0.0;
                for (int j = 0; j < Param_count; j++)
                    prediction[i] += model_linear_k[i][j] * telemetry[j].getFromTail(0);
            }
            for (int i = 0; i < 3; i++)
                error[i] = prediction[i] - angular_derivatives[i].getFromTail(0);
        }

        string[] importance_str = new string[Param_count];

        void set_inportance()
        {
            importance_str[(int)FCharacter.pitch] = (model_parameters_importance[0][(int)FCharacter.pitch] = 1.0).ToString("G8");
            importance_str[(int)FCharacter.aoa_pitch] = (model_parameters_importance[0][(int)FCharacter.aoa_pitch] = 1.0).ToString("G8");
            importance_str[(int)FCharacter.aoa_mul_pitch] = (model_parameters_importance[0][(int)FCharacter.aoa_mul_pitch] = 1e3).ToString("G8");
            importance_str[(int)FCharacter.com_horiz] = (model_parameters_importance[0][(int)FCharacter.com_horiz] = 1e3).ToString("G8");
            importance_str[(int)FCharacter.dyn_pressure] = (model_parameters_importance[0][(int)FCharacter.dyn_pressure] = 1e6).ToString("G8");
        }

        int solve_cycle_counter = 0;
        Thread least_squares_thread = null;
        void solve()
        {
            solve_cycle_counter = (solve_cycle_counter + 1) % Steps_remembered;
            if (dt_stable < Steps_remembered + 1 || solve_cycle_counter != 0)
                return;

            if (least_squares_thread == null)
            {
                least_squares_thread = new Thread(new ThreadStart(do_solve));
                prepare_matrixes();
                least_squares_thread.Start();
            }
            else
                if (!least_squares_thread.IsAlive && least_squares_thread.ThreadState == ThreadState.Stopped)
                {
                    least_squares_thread = new Thread(new ThreadStart(do_solve));
                    prepare_matrixes();
                    least_squares_thread.Start();
                }
        }

        //
        // Least squares section
        //
        bool[] non_zero;
        Matrix tel_matrix;
        Matrix ang_dev_pitch;

        void prepare_matrixes()
        {
            non_zero = new bool[Param_count];            // get non-zero parameters for pitch
            int non_zero_pars = 0;
            for (int j = 0; j < Param_count; j++)
                for (int i = 0; i < Steps_remembered; i++)
                {
                    if (telemetry[j][i] * model_parameters_importance[0][j] != 0.0)
                    {
                        non_zero[j] = true;
                        non_zero_pars++;
                        break;
                    }
                }
            if (non_zero_pars == 0)
                return;

            tel_matrix = new Matrix(Steps_remembered, non_zero_pars);
            for (int i = 0; i < Steps_remembered; i++)
            {
                for (int j = 0, k = 0; j < Param_count; j++)
                    if (non_zero[j])
                    {
                        tel_matrix[i, k] = telemetry[j][i] * model_parameters_importance[0][j];
                        k++;
                    }
            }

            ang_dev_pitch = new Matrix(Steps_remembered, 1);
            for (int i = 0; i < Steps_remembered; i++)
                ang_dev_pitch[i, 0] = angular_derivatives[(int)FControl.avd_pitch][i];
        }

        void do_solve()
        {
            try
            {
                // Linear least squares method
                // Parameter vector = (Xt * X)^-1 * Xt * y

                //
                // Pitch
                //                
                Matrix tel_m_transposed = Matrix.Transpose(tel_matrix);
                Matrix inverted = (tel_m_transposed * tel_matrix).Invert();                
                Matrix result_vector = inverted * tel_m_transposed * ang_dev_pitch;

                // Apply results
                for (int i = 0, j = 0; i < Param_count; i++)
                    if (non_zero[i])
                    {
                        double new_koef = result_vector[j, 0];
                        j++;
                        if (!double.IsInfinity(new_koef) && !double.IsNaN(new_koef))
                            model_linear_k[(int)FControl.avd_pitch][i] =
                                (new_koef + model_linear_k[(int)FControl.avd_pitch][i] * solve_count) / (solve_count + 1);
                    }

                solve_count = Math.Min(solve_count + 1, solve_memory);
            }
            catch (Exception e)
            {
                Debug.Log("[Autopilot]: " + e.Message);
            }
        }

        public static double derivative1(double y0, double y1, double y2, double dt)    // first derivative
        {
            return (y0 - 4 * y1 + 3 * y2) / dt * 0.5;
        }

        public static double derivative2(double y0, double y1, double y2, double dt)    // second derivative
        {
            return (y0 - 2 * y1 + y2) / dt / dt;
        }


        //
        // GUI
        //

        //GUIStyle labelstyle = new GUIStyle(GUI.skin.label);

        public bool gui_shown = false;
        public void toggleGUI()
        {
            gui_shown = !gui_shown;
        }

        protected Rect window = new Rect(250.0f, 50.0f, 450.0f, 250.0f);

        public void drawGUI()
        {
            if (!gui_shown)
                return;
            window = GUILayout.Window(77347, window, _drawGUI, "FlightModel");
        }

        void _drawGUI(int id)
        {
            GUILayout.BeginVertical();

            for (int i = 0; i < Param_count; i++)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label(Enum.GetName(typeof(FCharacter), (FCharacter)i) + " = " +
                    telemetry[i].getFromTail(0).ToString("G8"));
                for (int j = 0; j < 1; j++)
                    GUILayout.Label("Linear K = " + model_linear_k[j][i].ToString("G8"));
                GUILayout.EndHorizontal();
            }
            for (int i = 0; i < 3; i++)
            {
                GUILayout.Label(Enum.GetName(typeof(FControl), (FControl)i) + " = " +
                    angular_derivatives[i].getFromTail(0).ToString("G8") + " error = " + error[i].ToString("G8"));
            }
            for (int i = 0; i < Param_count; i++)
            {
                if (importance_str[i] == null)
                    continue;
                GUILayout.BeginHorizontal();
                try
                {
                    GUILayout.Label(Enum.GetName(typeof(FCharacter), (FCharacter)i) + " importance = ");
                    importance_str[i] = GUILayout.TextField(importance_str[i]);
                    model_parameters_importance[0][i] = double.Parse(importance_str[i]);
                }
                catch { }
                GUILayout.EndHorizontal();
            }

            GUILayout.EndVertical();
            GUI.DragWindow();
        }
	}
}