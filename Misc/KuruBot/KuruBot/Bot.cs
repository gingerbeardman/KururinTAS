﻿using Priority_Queue;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace KuruBot
{
    class Bot
    {
        Flooding f = null;
        Physics p = null;
        float[,] current_cost_map = null;

        public Bot(Map m, Physics p, Flooding.Pixel start, Flooding.Pixel end)
        {
            f = new Flooding(m, start, end);
            this.p = p;
        }

        public Flooding.Pixel GetPixelStart() { return f.PixelStart; }
        public Flooding.Pixel GetPixelEnd() { return f.PixelEnd; }

        public void ComputeNewCostMaps(float gwb_mult, float wgm_mult, Flooding.WallClipSetting wcs)
        {
            current_cost_map = f.ComputeCostMap(gwb_mult, wgm_mult, wcs);
        }

        public float[,] GetCurrentCostMap()
        {
            return current_cost_map;
        }

        // /!\ For efficiency reason, we use a class instead of a struct. Copies need to be performed manually when needed.
        class StateData
        {
            public StateData(HelirinState es, float w, float c, Action? a, HelirinState ps)
            {
                exact_state = es;
                weight = w;
                cost = c;
                action = a;
                previous_state = ps;
            }
            public HelirinState exact_state;
            public float weight;
            public float cost;
            public Action? action;
            public HelirinState previous_state;
        }

        const int pos_reduction = 0x10000 / 64; // 1/64 px
        const int bump_reduction = 0x10000 / 64; // 1/64 px/frame
        const short rot_reduction = Physics.default_srate;
        const short rot_rate_reduction = Physics.default_srate;

        HelirinState NormaliseState (HelirinState st)
        {
            st = st.ShallowCopy();

            int wall_dist = (int)f.DistToWall(Physics.pos_to_px(st.xpos), Physics.pos_to_px(st.ypos)) / Map.tile_size + 1;
            int pos_reduction = Bot.pos_reduction * wall_dist;
            int bump_reduction = Bot.bump_reduction * wall_dist;

            st.xpos = (int)Math.Round((float)st.xpos / pos_reduction) * pos_reduction;
            st.ypos = (int)Math.Round((float)st.ypos / pos_reduction) * pos_reduction;
            st.xb   = (int)Math.Round((float)st.xb / bump_reduction) * bump_reduction;
            st.yb   = (int)Math.Round((float)st.yb / bump_reduction) * bump_reduction;
            st.rot  = (short)((int)Math.Round((float)st.rot / rot_reduction) * rot_reduction);
            st.rot_rate = (short)((int)Math.Round((float)st.rot_rate / rot_rate_reduction) * rot_rate_reduction);

            return st;
        }

        float GetCost(int xpos, int ypos)
        {
            short xpix = Physics.pos_to_px(xpos);
            short ypix = Physics.pos_to_px(ypos);
            return f.Cost(current_cost_map, xpix, ypix);
        }

        bool IsOutOfSearchSpace(int xpos, int ypos)
        {
            short xpix = Physics.pos_to_px(xpos);
            short ypix = Physics.pos_to_px(ypos);
            return xpix < f.PixelStart.x || xpix > f.PixelEnd.x || ypix < f.PixelStart.y || ypix > f.PixelEnd.y;
        }

        public Action[] Solve (HelirinState init)
        {
            SimplePriorityQueue<HelirinState> q = new SimplePriorityQueue<HelirinState>();
            Dictionary<HelirinState, StateData> data = new Dictionary<HelirinState, StateData>();

            // Init
            HelirinState norm_init = NormaliseState(init);
            float cost = GetCost(init.xpos, init.ypos);
            float weight = 0;
            q.Enqueue(norm_init, cost);
            data.Add(norm_init, new StateData(init, weight, cost, null, null));

            // A*
            HelirinState result = null;
            while (q.Count > 0 && result == null)
            {
                HelirinState norm_st = q.Dequeue();
                StateData st_data = data[norm_st];
                weight = st_data.weight + 1;
                for (int i = 0; i < 25; i++)
                {
                    Action a = (Action)i;
                    HelirinState nst = p.Next(st_data.exact_state, a);
                    HelirinState norm_nst = NormaliseState(nst);

                    // Out of search space / Loose ?
                    if (nst.gs == GameState.Loose || IsOutOfSearchSpace(nst.xpos, nst.ypos))
                        continue;

                    // Add/update ?
                    cost = GetCost(nst.xpos, nst.ypos);
                    float total_cost = cost + weight;
                    StateData old = null;
                    data.TryGetValue(norm_nst, out old);
                    if (old == null || total_cost < old.cost + old.weight)
                    {
                        StateData nst_data = new StateData(nst, weight, cost, a, norm_st);
                        data[norm_nst] = nst_data;

                        // Win?
                        if (nst.gs == GameState.Win)
                        {
                            result = norm_nst;
                            break;
                        }

                        if (old == null)
                            q.Enqueue(norm_nst, total_cost);
                        else
                            q.UpdatePriority(norm_nst, total_cost);
                    }
                }
            }

            // Retrieve full path
            if (result == null)
                return null;
            List<Action> res = new List<Action>();
            while (result != null)
            {
                StateData sd = data[result];
                if (sd.action.HasValue)
                    res.Add(sd.action.Value);
                result = sd.previous_state;
            }
            res.Reverse();
            return res.ToArray();
        }
    }
}
