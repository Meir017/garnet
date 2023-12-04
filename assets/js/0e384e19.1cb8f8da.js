"use strict";(self.webpackChunkwebsite=self.webpackChunkwebsite||[]).push([[9671],{7876:(e,t,n)=>{n.r(t),n.d(t,{assets:()=>c,contentTitle:()=>i,default:()=>u,frontMatter:()=>a,metadata:()=>o,toc:()=>l});var s=n(5893),r=n(1151);const a={id:"intro",sidebar_label:"Intro",title:"Welcome to Garnet",slug:"/"},i="Welcome to Garnet",o={id:"intro",title:"Welcome to Garnet",description:"Garnet is a new remote cache-store from Microsoft Research, that is designed to be extremely fast, extensible,",source:"@site/docs/intro.md",sourceDirName:".",slug:"/",permalink:"/docs/",draft:!1,unlisted:!1,editUrl:"https://github.com/microsoft/Garnet/tree/main/website/docs/intro.md",tags:[],version:"current",frontMatter:{id:"intro",sidebar_label:"Intro",title:"Welcome to Garnet",slug:"/"},sidebar:"garnetDocSidebar",next:{title:"Releases",permalink:"/docs/welcome/releases"}},c={},l=[{value:"API Coverage",id:"api-coverage",level:2},{value:"Platforms Supported",id:"platforms-supported",level:2}];function d(e){const t={h1:"h1",h2:"h2",li:"li",p:"p",ul:"ul",...(0,r.a)(),...e.components};return(0,s.jsxs)(s.Fragment,{children:[(0,s.jsx)(t.h1,{id:"welcome-to-garnet",children:"Welcome to Garnet"}),"\n",(0,s.jsx)(t.p,{children:"Garnet is a new remote cache-store from Microsoft Research, that is designed to be extremely fast, extensible,\nand low latency. Garnet is thread-scalable within a single node. It also supports sharded cluster execution,\nwith replication, checkpointing, failover, and transactions. It can operate over main memory as well as\ntiered storage (such as SSD and Azure Storage). Garnet supports a rich API surface and a powerful extensibility\nmodel."}),"\n",(0,s.jsx)(t.p,{children:"Garnet uses Redis RESP as its primary wire protocol. Thus, one can use Garnet with unmodified Redis clients\navailable in every programming language, for example, with StackExchange.Redis in C#. Compared to Redis\nservers, you get much better performance, latency, extensibility, and durability features."}),"\n",(0,s.jsx)(t.p,{children:"Garnet is production ready: at Microsoft, many internal first-party and platforms teams have successfully\ndeployed versions of Garnet in production."}),"\n",(0,s.jsx)(t.p,{children:"Garnet offers the following key advantages:"}),"\n",(0,s.jsxs)(t.ul,{children:["\n",(0,s.jsx)(t.li,{children:"Orders-of-magnitude better server throughput (ops/sec) with small batches, as we increase the number of client sessions."}),"\n",(0,s.jsx)(t.li,{children:"Lower single operation latency (sub-100 microsecs median latency, and sub-700 microsecs 99th percentile latency on\ncommodity cloud (Azure) machines with accelerated TCP enabled, on both Windows and Linux."}),"\n",(0,s.jsx)(t.li,{children:"Better scalability as we increase the number of clients, with or without client-side batching."}),"\n",(0,s.jsx)(t.li,{children:"The ability to use all CPU/memory resources of a server machine with a single shared-memory server instance\n(no intra-node cluster needed)."}),"\n",(0,s.jsx)(t.li,{children:"Database features such as fast checkpointing and recovery, plus reliable pub/sub."}),"\n",(0,s.jsx)(t.li,{children:"Support for larger-than-memory datasets, spilling to local and cloud storage devices."}),"\n",(0,s.jsx)(t.li,{children:'Support for multi-node static hash partitioning (Redis "cluster" mode), state migration, and replication.'}),"\n",(0,s.jsx)(t.li,{children:"Well tested with a comprehensive test suite (1000+ unit tests across Garnet and its storage layer Tsavorite)."}),"\n",(0,s.jsx)(t.li,{children:"A C# codebase that is easy to evolve and extend."}),"\n"]}),"\n",(0,s.jsx)(t.p,{children:"If you use, or intend to use, Redis (or a similar remote cache/store) in your application or service, and wish\nto lower costs and move to a future-proof design based on state-of-the-art Microsoft Research technology,\nGarnet is the system for you."}),"\n",(0,s.jsx)(t.h2,{id:"api-coverage",children:"API Coverage"}),"\n",(0,s.jsx)(t.p,{children:"Garnet supports a large (and growing) subset of the Redis REST API surface, including:"}),"\n",(0,s.jsxs)(t.ul,{children:["\n",(0,s.jsx)(t.li,{children:"Raw string operations such as GET, SET, MGET, MSET, GETSET, SETEX, DEL, EXISTS, RENAME, EXPIRE, SET variants (set if exists, set if not exists)."}),"\n",(0,s.jsx)(t.li,{children:"Numeric operations such as INCR, INCRBY, DECR, DECRBY."}),"\n",(0,s.jsx)(t.li,{children:"Checkpoint/recovery ops such as SAVE, LASTSAVE, BGSAVE."}),"\n",(0,s.jsx)(t.li,{children:"Basic admin ops such as PING, QUIT, CONFIG, RESET, TIME."}),"\n",(0,s.jsx)(t.li,{children:"Advanced data structures such as List, Hash, Set, Sorted Set, and Geo."}),"\n",(0,s.jsx)(t.li,{children:"Analytics APIs such as Hyperloglog and Bitmap."}),"\n",(0,s.jsx)(t.li,{children:"Publish/subscribe."}),"\n",(0,s.jsx)(t.li,{children:"Transactions."}),"\n"]}),"\n",(0,s.jsx)(t.p,{children:"The list is growing over time, and we would love to hear from you on what APIs you want the most!"}),"\n",(0,s.jsx)(t.p,{children:"Further, Garnet supports a powerful custom operator framework whereby you can register custom\nC# data structures and read-modify-write operations on the server, and access them via an\nextension of RESP."}),"\n",(0,s.jsx)(t.h2,{id:"platforms-supported",children:"Platforms Supported"}),"\n",(0,s.jsx)(t.p,{children:"Garnet server is based on high-performance .NET technology written from the ground up with performance\nin mind. Garnet has been extensively tested to work equally efficiently on both Linux and Windows,\nand on commodity Azure hardware as well as edge devices."}),"\n",(0,s.jsx)(t.p,{children:"One can also view Garnet as an incredibly fast remote .NET data structure server, that can easily\nbe extended to support the rich plethora of C# libraries, so we can go far beyond the core API\nin future. Garnet's storage layer is called Tsavorite, which supports for various backing\nstorage devices such as fast local SSD drives and Azure Storage. It has devices optimized for\nWindows and Linux as well. Finally, Garnet supports TLS for secure connections."})]})}function u(e={}){const{wrapper:t}={...(0,r.a)(),...e.components};return t?(0,s.jsx)(t,{...e,children:(0,s.jsx)(d,{...e})}):d(e)}},1151:(e,t,n)=>{n.d(t,{Z:()=>o,a:()=>i});var s=n(7294);const r={},a=s.createContext(r);function i(e){const t=s.useContext(a);return s.useMemo((function(){return"function"==typeof e?e(t):{...t,...e}}),[t,e])}function o(e){let t;return t=e.disableParentContext?"function"==typeof e.components?e.components(r):e.components||r:i(e.components),s.createElement(a.Provider,{value:t},e.children)}}}]);